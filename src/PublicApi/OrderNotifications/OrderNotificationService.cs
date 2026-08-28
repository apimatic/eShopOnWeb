using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Twilio;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.OrderNotifications;

public sealed class OrderNotificationService
{
    private static readonly SemaphoreSlim ResendGate = new(1, 1);
    private readonly CatalogContext _db;
    private readonly ITwilioGateway _twilio;
    private readonly ILogger<OrderNotificationService> _logger;

    public OrderNotificationService(CatalogContext db, ITwilioGateway twilio,
        ILogger<OrderNotificationService> logger)
    {
        _db = db;
        _twilio = twilio;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order) =>
        await SendToCurrentContactsAsync(order, NotificationKind.OrderPlaced,
            $"Your eShop order {order.Id} has been placed.");

    public async Task NotifyOrderDispatchedAsync(Order order)
    {
        var contacts = await CurrentContactsAsync(order.BuyerId);
        foreach (var contact in contacts)
        {
            await CreateAndSendAsync(order, contact, NotificationKind.OrderDispatched,
                $"Your eShop order {order.Id} is on its way.");
            await CreateAndScheduleAsync(order, contact, NotificationKind.DeliveryFollowUp,
                $"How did delivery of your eShop order {order.Id} go?", DateTimeOffset.UtcNow.AddDays(3));
        }
    }

    public async Task NotifyOrderCancelledAsync(Order order)
    {
        var followUps = await _db.OrderNotifications
            .Where(x => x.OrderId == order.Id && x.Kind == NotificationKind.DeliveryFollowUp &&
                        x.ProviderMessageSid != null && x.ProviderStatus != "canceled")
            .ToListAsync();

        await RefreshAsync(followUps);
        foreach (var followUp in followUps.Where(x => x.ProviderStatus is "scheduled" or "queued" or "accepted" or "cancel-pending"))
            await CancelScheduledAsync(followUp);

        await SendToCurrentContactsAsync(order, NotificationKind.OrderCancelled,
            $"Your eShop order {order.Id} was cancelled.");
    }

    public async Task RefreshAsync(IReadOnlyCollection<OrderNotification> notifications)
    {
        var changed = false;
        foreach (var notification in notifications.Where(x => x.ProviderMessageSid is not null))
        {
            try
            {
                var provider = await _twilio.FetchMessageAsync(notification.ProviderMessageSid!, CancellationToken.None);
                notification.RefreshProviderState(provider.Status, provider.ErrorCode, DateTimeOffset.UtcNow);
                changed = true;
            }
            catch (Exception)
            {
                _logger.LogWarning("Could not refresh Twilio state for notification {NotificationId}.", notification.Id);
            }
        }

        if (changed) await _db.SaveChangesAsync(CancellationToken.None);
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey)
    {
        await ResendGate.WaitAsync();
        try
        {
            var existing = await _db.OrderNotifications.SingleOrDefaultAsync(x =>
                x.OriginalNotificationId == notificationId && x.IdempotencyKey == idempotencyKey);
            if (existing is not null) return ResendResult.Success(existing.Id);

            var original = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId);
            if (original is null) return ResendResult.NotFound();

            if (original.ProviderMessageSid is not null) await RefreshAsync(new[] { original });
            if (!IsFailedOutcome(original.ProviderStatus) || original.ContentDisposed || string.IsNullOrEmpty(original.Body))
                return ResendResult.NotEligible();

            var contact = await _db.ContactNumbers.SingleOrDefaultAsync(x =>
                x.Id == original.ContactNumberId && x.BuyerId == original.BuyerId);
            if (contact is null) return ResendResult.ContactRemoved();

            var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == original.OrderId);
            if (order is null) return ResendResult.NotFound();
            if (order.Status == OrderStatus.Cancelled &&
                original.Kind is NotificationKind.OrderDispatched or NotificationKind.DeliveryFollowUp)
                return ResendResult.NotEligible();

            var resend = new OrderNotification(order.Id, contact.Id, order.BuyerId,
                original.Kind, original.Body, DateTimeOffset.UtcNow, original.Id, idempotencyKey);
            _db.OrderNotifications.Add(resend);
            try
            {
                await _db.SaveChangesAsync(CancellationToken.None);
            }
            catch (DbUpdateException)
            {
                _db.Entry(resend).State = EntityState.Detached;
                existing = await _db.OrderNotifications.SingleOrDefaultAsync(x =>
                    x.OriginalNotificationId == notificationId && x.IdempotencyKey == idempotencyKey);
                if (existing is not null) return ResendResult.Success(existing.Id);
                throw;
            }
            await SendPersistedAsync(resend, contact.CanonicalNumber);
            return ResendResult.Success(resend.Id);
        }
        finally
        {
            ResendGate.Release();
        }
    }

    public async Task<ContentDisposalResult> DisposeContentAsync(int notificationId)
    {
        var notification = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId);
        if (notification is null) return ContentDisposalResult.NotFound;
        if (notification.ContentDisposed) return ContentDisposalResult.Success;

        try
        {
            if (notification.ProviderMessageSid is not null)
            {
                var provider = await _twilio.RedactMessageAsync(notification.ProviderMessageSid, CancellationToken.None);
                if (!string.IsNullOrEmpty(provider.Body)) return ContentDisposalResult.ProviderFailed;
                notification.RefreshProviderState(provider.Status, provider.ErrorCode, DateTimeOffset.UtcNow);
            }
        }
        catch (Exception)
        {
            _logger.LogWarning("Twilio content redaction failed for notification {NotificationId}.", notification.Id);
            return ContentDisposalResult.ProviderFailed;
        }

        notification.DisposeContent(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(CancellationToken.None);
        return ContentDisposalResult.Success;
    }

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var providerMessages = await _twilio.ListMessagesAsync(from, to, cancellationToken);
        var local = await _db.OrderNotifications.AsNoTracking()
            .Where(x => x.CreatedAt >= from && x.CreatedAt <= to)
            .ToListAsync(cancellationToken);
        var localBySid = local.Where(x => x.ProviderMessageSid != null)
            .ToDictionary(x => x.ProviderMessageSid!, StringComparer.Ordinal);
        var providerBySid = providerMessages.ToDictionary(x => x.Sid, StringComparer.Ordinal);
        var entries = new List<ReconciliationEntryResponse>();

        foreach (var provider in providerMessages.OrderBy(x => x.DateCreated))
        {
            localBySid.TryGetValue(provider.Sid, out var match);
            entries.Add(new ReconciliationEntryResponse(provider.Sid, match?.Id, match?.OrderId,
                match is null ? "provider-only" : "both", provider.Status, match?.ProviderStatus,
                provider.DateSent, match?.CreatedAt));
        }

        foreach (var application in local.Where(x => x.ProviderMessageSid is null || !providerBySid.ContainsKey(x.ProviderMessageSid)))
        {
            entries.Add(new ReconciliationEntryResponse(application.ProviderMessageSid, application.Id,
                application.OrderId, "application-only", null, application.ProviderStatus, null, application.CreatedAt));
        }

        return new ReconciliationResponse(from, to, providerMessages.Count, local.Count, entries);
    }

    public async Task RetryFollowUpCancellationAsync(OrderNotification notification)
    {
        if (notification.Kind != NotificationKind.DeliveryFollowUp || notification.ProviderMessageSid is null) return;
        await CancelScheduledAsync(notification);
    }

    private async Task SendToCurrentContactsAsync(Order order, NotificationKind kind, string body)
    {
        foreach (var contact in await CurrentContactsAsync(order.BuyerId))
            await CreateAndSendAsync(order, contact, kind, body);
    }

    private Task<List<ContactNumber>> CurrentContactsAsync(string buyerId) =>
        _db.ContactNumbers.Where(x => x.BuyerId == buyerId).ToListAsync();

    private async Task CreateAndSendAsync(Order order, ContactNumber contact, NotificationKind kind, string body)
    {
        var notification = new OrderNotification(order.Id, contact.Id, order.BuyerId, kind, body, DateTimeOffset.UtcNow);
        _db.OrderNotifications.Add(notification);
        await _db.SaveChangesAsync(CancellationToken.None);
        await SendPersistedAsync(notification, contact.CanonicalNumber);
    }

    private async Task SendPersistedAsync(OrderNotification notification, string destination)
    {
        try
        {
            var provider = await _twilio.SendMessageAsync(destination, notification.Body!, CancellationToken.None);
            notification.RecordProviderMessage(provider.Sid, provider.Status, provider.ErrorCode, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            notification.RecordProviderFailure((ex as TwilioApiException)?.ProviderCode, DateTimeOffset.UtcNow);
            _logger.LogWarning("Twilio send failed for notification {NotificationId}.", notification.Id);
        }

        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task CreateAndScheduleAsync(Order order, ContactNumber contact, NotificationKind kind,
        string body, DateTimeOffset sendAt)
    {
        var notification = new OrderNotification(order.Id, contact.Id, order.BuyerId, kind, body, DateTimeOffset.UtcNow);
        _db.OrderNotifications.Add(notification);
        await _db.SaveChangesAsync(CancellationToken.None);
        try
        {
            var provider = await _twilio.ScheduleMessageAsync(contact.CanonicalNumber, body, sendAt, CancellationToken.None);
            notification.RecordProviderMessage(provider.Sid, provider.Status, provider.ErrorCode, DateTimeOffset.UtcNow, sendAt);
        }
        catch (Exception ex)
        {
            notification.RecordProviderFailure((ex as TwilioApiException)?.ProviderCode, DateTimeOffset.UtcNow);
            _logger.LogWarning("Twilio scheduling failed for notification {NotificationId}.", notification.Id);
        }

        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task CancelScheduledAsync(OrderNotification notification)
    {
        try
        {
            var provider = await _twilio.CancelMessageAsync(notification.ProviderMessageSid!, CancellationToken.None);
            notification.RefreshProviderState(provider.Status, provider.ErrorCode, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            notification.RefreshProviderState("cancel-pending", (ex as TwilioApiException)?.ProviderCode, DateTimeOffset.UtcNow);
            _logger.LogWarning("Twilio cancellation will be retried for notification {NotificationId}.", notification.Id);
        }

        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private static bool IsFailedOutcome(string status) => status is
        "failed" or "undelivered" or "partially_delivered" or "provider-failed";

}

public sealed record ResendResult(bool Succeeded, int? NotificationId, string? Error)
{
    public static ResendResult Success(int id) => new(true, id, null);
    public static ResendResult NotFound() => new(false, null, "not-found");
    public static ResendResult NotEligible() => new(false, null, "not-eligible");
    public static ResendResult ContactRemoved() => new(false, null, "contact-removed");
}

public enum ContentDisposalResult
{
    Success,
    NotFound,
    ProviderFailed
}
