using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.OrderNotifications;

public sealed class OrderNotificationService
{
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);
    private static readonly SemaphoreSlim ResendLock = new(1, 1);
    private readonly CatalogContext _db;
    private readonly ITwilioClient _twilio;

    public OrderNotificationService(CatalogContext db, ITwilioClient twilio)
    {
        _db = db;
        _twilio = twilio;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken) =>
        NotifyActiveNumbersAsync(order, NotificationKind.OrderPlaced,
            $"Your eShop order #{order.Id} has been placed.", null, cancellationToken);

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken)
    {
        await NotifyActiveNumbersAsync(order, NotificationKind.OrderDispatched,
            $"Your eShop order #{order.Id} is on its way.", null, cancellationToken);
        await NotifyActiveNumbersAsync(order, NotificationKind.DeliveryFollowUp,
            $"How did delivery of your eShop order #{order.Id} go?", DateTimeOffset.UtcNow.Add(FollowUpDelay),
            cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken)
    {
        await CancelScheduledFollowUpsAsync(order.Id, cancellationToken);
        await NotifyActiveNumbersAsync(order, NotificationKind.OrderCancelled,
            $"Your eShop order #{order.Id} has been cancelled.", null, cancellationToken);
    }

    public async Task<bool> CancelScheduledMessagesForContactAsync(int contactNumberId,
        CancellationToken cancellationToken)
    {
        var notifications = await _db.OrderNotifications
            .Where(x => x.ContactNumberId == contactNumberId &&
                        x.Kind == NotificationKind.DeliveryFollowUp &&
                        (x.ProviderStatus == "scheduled" || x.ProviderStatus == "cancellation_pending"))
            .ToListAsync(cancellationToken);

        foreach (var notification in notifications)
        {
            if (!await TryCancelAsync(notification, cancellationToken))
            {
                await _db.SaveChangesAsync(CancellationToken.None);
                return false;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task RefreshAsync(IReadOnlyCollection<OrderNotification> notifications,
        CancellationToken cancellationToken)
    {
        foreach (var notification in notifications.Where(x => x.ProviderMessageSid != null))
        {
            try
            {
                var provider = await _twilio.FetchMessageAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.RecordProviderState(provider.Sid, provider.Status, provider.ErrorCode);
            }
            catch (Exception exception) when (IsProviderFailure(exception))
            {
                // The last known provider state remains reportable when a refresh is temporarily unavailable.
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ResendResult> ResendAsync(int sourceNotificationId, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await ResendLock.WaitAsync(cancellationToken);
        try
        {
            var prior = await _db.NotificationResends.SingleOrDefaultAsync(
                x => x.SourceNotificationId == sourceNotificationId && x.IdempotencyKey == idempotencyKey,
                cancellationToken);
            if (prior != null)
            {
                return new ResendResult(prior.NotificationId, true, null);
            }

            var source = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == sourceNotificationId,
                cancellationToken);
            if (source == null)
            {
                return new ResendResult(0, false, "not_found");
            }

            if (source.ProviderMessageSid != null)
            {
                try
                {
                    var current = await _twilio.FetchMessageAsync(source.ProviderMessageSid, cancellationToken);
                    source.RecordProviderState(current.Sid, current.Status, current.ErrorCode);
                }
                catch (Exception exception) when (IsProviderFailure(exception))
                {
                    // Use the persisted outcome if the authoritative refresh is temporarily unavailable.
                }
            }

            if (!CanResend(source.ProviderStatus))
            {
                return new ResendResult(0, false, "not_failed");
            }

            if (source.ContentDisposed || string.IsNullOrWhiteSpace(source.Body))
            {
                return new ResendResult(0, false, "content_disposed");
            }

            if (source.Kind == NotificationKind.DeliveryFollowUp)
            {
                var orderStatus = await _db.Orders.Where(x => x.Id == source.OrderId)
                    .Select(x => x.Status).SingleAsync(cancellationToken);
                if (orderStatus == OrderStatus.Cancelled)
                {
                    return new ResendResult(0, false, "order_cancelled");
                }
            }

            var contact = await _db.ContactNumbers.SingleOrDefaultAsync(
                x => x.Id == source.ContactNumberId && x.ShopperId == source.ShopperId,
                cancellationToken);
            if (contact == null)
            {
                return new ResendResult(0, false, "contact_removed");
            }

            var notification = new OrderNotification(source.OrderId, source.ShopperId, contact.Id,
                source.Kind, source.Body, null, source.Id);
            _db.OrderNotifications.Add(notification);
            await _db.SaveChangesAsync(cancellationToken);

            // Persist the caller's key before talking to the provider. Twilio's classic send API has no
            // provider-side idempotency key, so this ordering guarantees a repeated API request cannot send twice.
            _db.NotificationResends.Add(new NotificationResend(source.Id, idempotencyKey, notification.Id));
            await _db.SaveChangesAsync(cancellationToken);

            await SendPersistedAsync(notification, contact.PhoneNumber, cancellationToken);
            return new ResendResult(notification.Id, false, null);
        }
        finally
        {
            ResendLock.Release();
        }
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId,
            cancellationToken);
        if (notification == null)
        {
            return false;
        }

        if (notification.ContentDisposed)
        {
            return true;
        }

        if (notification.ProviderMessageSid != null)
        {
            var provider = await _twilio.RedactMessageAsync(notification.ProviderMessageSid, cancellationToken);
            if (!string.IsNullOrEmpty(provider.Body))
            {
                throw new TwilioProviderException(502);
            }
            notification.RecordProviderState(provider.Sid, provider.Status, provider.ErrorCode);
        }

        notification.DisposeContent();
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<ReconciliationEntry>> ReconcileAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        var provider = await _twilio.ListMessagesAsync(from, to, cancellationToken);
        var local = await _db.OrderNotifications
            .Where(x => x.CreatedAt >= from && x.CreatedAt <= to)
            .ToListAsync(cancellationToken);

        var providerBySid = provider.Where(x => !string.IsNullOrEmpty(x.Sid)).ToDictionary(x => x.Sid);
        var localBySid = local.Where(x => !string.IsNullOrEmpty(x.ProviderMessageSid))
            .ToDictionary(x => x.ProviderMessageSid!);
        var entries = new List<ReconciliationEntry>();

        foreach (var item in local)
        {
            providerBySid.TryGetValue(item.ProviderMessageSid ?? string.Empty, out var match);
            entries.Add(new ReconciliationEntry(item.Id, item.ProviderMessageSid, item.ProviderStatus,
                match?.Status, true, match != null, item.CreatedAt, match?.DateSent ?? match?.DateCreated));
        }

        foreach (var item in provider.Where(x => !localBySid.ContainsKey(x.Sid)))
        {
            entries.Add(new ReconciliationEntry(null, item.Sid, null, item.Status,
                false, true, null, item.DateSent ?? item.DateCreated));
        }

        return entries.OrderBy(x => x.ProviderTimestamp ?? x.LocalTimestamp).ToList();
    }

    public async Task RetryPendingCancellationsAsync(CancellationToken cancellationToken)
    {
        var pending = await _db.OrderNotifications
            .Where(x => x.ProviderStatus == "cancellation_pending" && x.ProviderMessageSid != null)
            .ToListAsync(cancellationToken);
        foreach (var notification in pending)
        {
            await TryCancelAsync(notification, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task NotifyActiveNumbersAsync(Order order, NotificationKind kind, string body,
        DateTimeOffset? scheduledFor, CancellationToken cancellationToken)
    {
        var contacts = await _db.ContactNumbers.Where(x => x.ShopperId == order.BuyerId)
            .ToListAsync(cancellationToken);
        foreach (var contact in contacts)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, contact.Id, kind, body, scheduledFor);
            _db.OrderNotifications.Add(notification);
            await _db.SaveChangesAsync(cancellationToken);
            await SendPersistedAsync(notification, contact.PhoneNumber, cancellationToken);
        }
    }

    private async Task SendPersistedAsync(OrderNotification notification, string destination,
        CancellationToken cancellationToken)
    {
        try
        {
            var provider = await _twilio.SendMessageAsync(destination, notification.Body!,
                notification.ScheduledFor, cancellationToken);
            notification.RecordProviderState(provider.Sid, provider.Status, provider.ErrorCode);
        }
        catch (Exception exception) when (IsProviderFailure(exception))
        {
            if (exception is TwilioProviderException or InvalidOperationException or FormatException)
            {
                notification.RecordProviderFailure((exception as TwilioProviderException)?.ProviderErrorCode);
            }
            else
            {
                // A connection loss or timeout after POST may mean the provider accepted the message.
                // Keep this outcome distinct so an operator cannot blindly resend and duplicate it.
                notification.RecordProviderOutcomeUnknown();
            }
        }

        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task CancelScheduledFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var scheduled = await _db.OrderNotifications
            .Where(x => x.OrderId == orderId && x.Kind == NotificationKind.DeliveryFollowUp &&
                        (x.ProviderStatus == "scheduled" || x.ProviderStatus == "cancellation_pending"))
            .ToListAsync(cancellationToken);
        foreach (var notification in scheduled)
        {
            await TryCancelAsync(notification, cancellationToken);
        }

        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<bool> TryCancelAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (notification.ProviderMessageSid == null)
        {
            return true;
        }

        try
        {
            var provider = await _twilio.CancelMessageAsync(notification.ProviderMessageSid, cancellationToken);
            notification.RecordProviderState(provider.Sid, provider.Status, provider.ErrorCode);
            return provider.Status == "canceled";
        }
        catch (Exception exception) when (IsProviderFailure(exception))
        {
            notification.MarkCancellationPending();
            return false;
        }
    }

    private static bool CanResend(string status) =>
        status is "failed" or "undelivered" or "provider_error";

    private static bool IsProviderFailure(Exception exception) =>
        exception is TwilioProviderException or HttpRequestException or TaskCanceledException or
            IOException or JsonException or InvalidOperationException or FormatException;
}

public sealed record ResendResult(int NotificationId, bool WasReplay, string? Error);

public sealed record ReconciliationEntry(int? NotificationId, string? ProviderMessageSid,
    string? LocalStatus, string? ProviderStatus, bool ExistsLocally, bool ExistsAtProvider,
    DateTimeOffset? LocalTimestamp, DateTimeOffset? ProviderTimestamp);
