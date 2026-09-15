using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Services;

public sealed class OrderNotificationCoordinator
{
    private static readonly SemaphoreSlim ResendLock = new(1, 1);
    private readonly CatalogContext _context;
    private readonly ISmsProvider _provider;

    public OrderNotificationCoordinator(CatalogContext context, ISmsProvider provider)
    {
        _context = context;
        _provider = provider;
    }

    public async Task<IReadOnlyList<OrderNotification>> SendForOrderAsync(Order order, NotificationKind kind,
        string body, DateTimeOffset? scheduledFor, CancellationToken cancellationToken)
    {
        var contacts = await _context.ContactNumbers
            .Where(x => x.BuyerId == order.BuyerId && x.RemovedAt == null)
            .ToListAsync(cancellationToken);
        var results = new List<OrderNotification>();

        foreach (var contact in contacts)
        {
            var notification = new OrderNotification(order.Id, contact.Id, order.BuyerId, kind, body,
                DateTimeOffset.UtcNow, scheduledFor);
            _context.OrderNotifications.Add(notification);
            await _context.SaveChangesAsync(cancellationToken);
            await SendPersistedAsync(notification, contact.PhoneNumber, scheduledFor, cancellationToken);
            results.Add(notification);
        }

        return results;
    }

    public async Task RefreshAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        var changed = false;
        foreach (var notification in notifications.Where(x => x.ProviderMessageSid is not null))
        {
            try
            {
                var providerMessage = await _provider.GetAsync(notification.ProviderMessageSid!, cancellationToken);
                Apply(notification, providerMessage);
                changed = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Reads return the last known state when Twilio is temporarily unavailable.
            }
        }
        if (changed) await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> CancelScheduledForOrderAsync(int orderId, int? contactNumberId,
        CancellationToken cancellationToken)
    {
        var notifications = await _context.OrderNotifications
            .Where(x => x.OrderId == orderId && x.Kind == NotificationKind.DeliveryFollowUp &&
                        (!contactNumberId.HasValue || x.ContactNumberId == contactNumberId.Value))
            .ToListAsync(cancellationToken);
        var allStopped = true;

        foreach (var notification in notifications)
        {
            if (notification.ProviderMessageSid is null) continue;
            try
            {
                var current = await _provider.GetAsync(notification.ProviderMessageSid, cancellationToken);
                Apply(notification, current);
                if (string.Equals(current.Status, "scheduled", StringComparison.OrdinalIgnoreCase))
                {
                    notification.RecordCancellationRequested(DateTimeOffset.UtcNow);
                    Apply(notification, await _provider.CancelAsync(notification.ProviderMessageSid, cancellationToken));
                }
                else if (!IsStopped(current.Status))
                {
                    allStopped = false;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                notification.RecordCancellationRequested(DateTimeOffset.UtcNow, (ex as SmsProviderException)?.ProviderErrorCode);
                allStopped = false;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        return allStopped;
    }

    public async Task<ResendResult> ResendAsync(int sourceNotificationId, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await ResendLock.WaitAsync(cancellationToken);
        try
        {
            var existing = await _context.NotificationResendRequests
                .SingleOrDefaultAsync(x => x.SourceNotificationId == sourceNotificationId &&
                                           x.IdempotencyKey == idempotencyKey, cancellationToken);
            if (existing is not null) return ResendResult.Success(existing.ResultNotificationId, true);

            var source = await _context.OrderNotifications.SingleOrDefaultAsync(x => x.Id == sourceNotificationId, cancellationToken);
            if (source is null) return ResendResult.NotFound();
            if (source.Body is null) return ResendResult.Invalid("Content has been disposed and cannot be resent.");

            if (source.ProviderMessageSid is not null)
                await RefreshAsync(new[] { source }, cancellationToken);
            if (!IsFailed(source.ProviderStatus))
                return ResendResult.Invalid("Only a failed or undelivered notification can be resent.");

            var contact = await _context.ContactNumbers.SingleOrDefaultAsync(x => x.Id == source.ContactNumberId, cancellationToken);
            if (contact is null || !contact.IsActive)
                return ResendResult.Invalid("The destination contact number is no longer active.");

            var order = await _context.Orders.SingleOrDefaultAsync(x => x.Id == source.OrderId, cancellationToken);
            if (order is null) return ResendResult.NotFound();
            if (order.Status == OrderStatus.Cancelled && source.Kind != NotificationKind.OrderCancelled)
                return ResendResult.Invalid("Notifications superseded by cancellation cannot be resent.");

            var notification = new OrderNotification(source.OrderId, source.ContactNumberId, source.BuyerId,
                NotificationKind.Resend, source.Body, DateTimeOffset.UtcNow, originalNotificationId: source.Id);
            _context.OrderNotifications.Add(notification);
            await _context.SaveChangesAsync(cancellationToken);

            _context.NotificationResendRequests.Add(new NotificationResendRequest(source.Id, idempotencyKey,
                notification.Id, DateTimeOffset.UtcNow));
            await _context.SaveChangesAsync(cancellationToken);
            await SendPersistedAsync(notification, contact.PhoneNumber, null, cancellationToken);
            return ResendResult.Success(notification.Id, false);
        }
        finally
        {
            ResendLock.Release();
        }
    }

    public async Task<DisposeContentResult> DisposeContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _context.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken);
        if (notification is null) return DisposeContentResult.NotFound();
        if (notification.ContentDisposedAt.HasValue) return DisposeContentResult.Success();

        if (notification.ProviderMessageSid is not null)
        {
            try
            {
                var result = await _provider.DisposeContentAsync(notification.ProviderMessageSid, cancellationToken);
                if (!string.IsNullOrEmpty(result.Body))
                    return DisposeContentResult.ProviderFailure("Twilio did not confirm that the message body was removed.");
                Apply(notification, result);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return DisposeContentResult.ProviderFailure(ex is SmsProviderException ? ex.Message : "Twilio content disposal failed.");
            }
        }

        notification.DisposeContent(DateTimeOffset.UtcNow);
        await _context.SaveChangesAsync(cancellationToken);
        return DisposeContentResult.Success();
    }

    public async Task<IReadOnlyList<ReconciliationItem>> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var providerMessages = (await _provider.ListAsync(from, to, cancellationToken)).ToDictionary(x => x.Sid);
        var local = await _context.OrderNotifications
            .Where(x => x.CreatedAt >= from && x.CreatedAt < to)
            .ToListAsync(cancellationToken);

        foreach (var notification in local.Where(x => x.ProviderMessageSid is not null && !providerMessages.ContainsKey(x.ProviderMessageSid)))
        {
            try
            {
                var providerMessage = await _provider.GetAsync(notification.ProviderMessageSid!, cancellationToken);
                providerMessages[providerMessage.Sid] = providerMessage;
                Apply(notification, providerMessage);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { }
        }
        await _context.SaveChangesAsync(cancellationToken);

        var localBySid = local.Where(x => x.ProviderMessageSid is not null)
            .ToDictionary(x => x.ProviderMessageSid!);
        var items = providerMessages.Values.Select(provider =>
        {
            localBySid.TryGetValue(provider.Sid, out var notification);
            return new ReconciliationItem(notification?.Id, provider.Sid, notification is null ? "provider-only" : "matched",
                notification?.ProviderStatus, provider.Status, provider.ErrorCode, provider.DateCreated, provider.DateSent);
        }).ToList();
        items.AddRange(local.Where(x => x.ProviderMessageSid is null || !providerMessages.ContainsKey(x.ProviderMessageSid))
            .Select(x => new ReconciliationItem(x.Id, x.ProviderMessageSid, "application-only", x.ProviderStatus,
                null, x.ProviderErrorCode, x.ProviderDateCreated, x.ProviderDateSent)));
        return items.OrderBy(x => x.ProviderDateCreated).ThenBy(x => x.NotificationId).ToList();
    }

    private async Task SendPersistedAsync(OrderNotification notification, string destination,
        DateTimeOffset? scheduledFor, CancellationToken cancellationToken)
    {
        try
        {
            var result = scheduledFor.HasValue
                ? await _provider.ScheduleAsync(destination, notification.Body!, scheduledFor.Value, cancellationToken)
                : await _provider.SendAsync(destination, notification.Body!, cancellationToken);
            Apply(notification, result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            notification.RecordProviderFailure((ex as SmsProviderException)?.ProviderErrorCode, DateTimeOffset.UtcNow);
        }
        await _context.SaveChangesAsync(cancellationToken);
    }

    private static void Apply(OrderNotification notification, SmsProviderMessage message) =>
        notification.RecordProviderState(message.Sid, message.Status, message.ErrorCode,
            message.DateCreated, message.DateSent, DateTimeOffset.UtcNow);

    private static bool IsFailed(string status) =>
        status.Equals("failed", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("undelivered", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("provider-request-failed", StringComparison.OrdinalIgnoreCase);

    private static bool IsStopped(string status) =>
        status.Equals("canceled", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("failed", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("undelivered", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("delivered", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("sent", StringComparison.OrdinalIgnoreCase);
}

public sealed record ResendResult(bool Succeeded, bool WasIdempotentReplay, int? NotificationId, string? Error, bool IsMissing)
{
    public static ResendResult Success(int id, bool replay) => new(true, replay, id, null, false);
    public static ResendResult Invalid(string error) => new(false, false, null, error, false);
    public static ResendResult NotFound() => new(false, false, null, null, true);
}

public sealed record DisposeContentResult(bool Succeeded, bool IsMissing, string? Error)
{
    public static DisposeContentResult Success() => new(true, false, null);
    public static DisposeContentResult NotFound() => new(false, true, null);
    public static DisposeContentResult ProviderFailure(string error) => new(false, false, error);
}

public sealed record ReconciliationItem(int? NotificationId, string? ProviderMessageSid, string Match,
    string? ApplicationStatus, string? ProviderStatus, int? ProviderErrorCode,
    DateTimeOffset? ProviderDateCreated, DateTimeOffset? ProviderDateSent);
