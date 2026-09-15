using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public sealed class OrderNotificationService
{
    public const string Placed = "placed";
    public const string Dispatched = "dispatched";
    public const string DeliveryFollowUp = "delivery-follow-up";
    public const string Cancelled = "cancelled";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> IdempotencyLocks = new();
    private static readonly string[] ResendableStatuses = { "failed", "undelivered", "provider-error" };
    private readonly CatalogContext _context;
    private readonly IMessageProvider _provider;
    private readonly ILogger<OrderNotificationService> _logger;

    public OrderNotificationService(CatalogContext context, IMessageProvider provider,
        ILogger<OrderNotificationService> logger)
    {
        _context = context;
        _provider = provider;
        _logger = logger;
    }

    public async Task NotifyCurrentContactsAsync(Order order, string kind, string body,
        DateTimeOffset? sendAt = null, CancellationToken cancellationToken = default)
    {
        var contacts = await _context.ContactNumbers
            .Where(x => x.BuyerId == order.BuyerId)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (var contact in contacts)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, contact.Id, kind,
                body, DateTimeOffset.UtcNow, sendAt);
            _context.OrderNotifications.Add(notification);
            await _context.SaveChangesAsync(cancellationToken);
            await TrySendAsync(notification, contact.PhoneNumber, cancellationToken);
        }
    }

    public async Task<bool> CancelOutstandingFollowUpsAsync(int orderId, int? contactNumberId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.OrderNotifications.Where(x =>
            x.OrderId == orderId && x.Kind == DeliveryFollowUp && x.ProviderMessageId != null
            && x.ProviderStatus != "canceled");
        if (contactNumberId.HasValue)
            query = query.Where(x => x.ContactNumberId == contactNumberId.Value);

        var notifications = await query.ToListAsync(cancellationToken);
        var allSafe = true;
        foreach (var notification in notifications)
        {
            notification.MarkCancellationPending();
        }
        if (notifications.Count > 0) await _context.SaveChangesAsync(cancellationToken);

        foreach (var notification in notifications)
        {
            if (!await TryCancelScheduledAsync(notification, cancellationToken)) allSafe = false;
        }
        return allSafe;
    }

    public async Task<bool> CancelOutstandingFollowUpsForContactAsync(int contactNumberId,
        CancellationToken cancellationToken = default)
    {
        var orderIds = await _context.OrderNotifications
            .Where(x => x.ContactNumberId == contactNumberId && x.Kind == DeliveryFollowUp
                && x.ProviderMessageId != null && x.ProviderStatus != "canceled")
            .Select(x => x.OrderId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var allSafe = true;
        foreach (var orderId in orderIds)
        {
            if (!await CancelOutstandingFollowUpsAsync(orderId, contactNumberId, cancellationToken))
                allSafe = false;
        }
        return allSafe;
    }

    public async Task RetryPendingCancellationsAsync(CancellationToken cancellationToken)
    {
        var pending = await _context.OrderNotifications
            .Where(x => x.ProviderStatus == "cancel-pending" && x.ProviderMessageId != null)
            .ToListAsync(cancellationToken);
        foreach (var notification in pending)
            await TryCancelScheduledAsync(notification, cancellationToken);
    }

    public async Task<IReadOnlyCollection<OrderNotification>> GetAndRefreshForOrderAsync(int orderId,
        CancellationToken cancellationToken = default)
    {
        var notifications = await _context.OrderNotifications
            .Where(x => x.OrderId == orderId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        await RefreshAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task RefreshAsync(IEnumerable<OrderNotification> notifications,
        CancellationToken cancellationToken = default)
    {
        var changed = false;
        foreach (var notification in notifications.Where(x => x.ProviderMessageId != null))
        {
            try
            {
                var state = await _provider.GetAsync(notification.ProviderMessageId!, cancellationToken);
                Apply(notification, state);
                changed = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                _logger.LogWarning("Could not refresh provider state for notification {NotificationId}.",
                    notification.Id);
            }
        }
        if (changed) await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<(OrderNotification? Notification, string? Error)> ResendAsync(int notificationId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var lockKey = $"{notificationId}:{idempotencyKey}";
        var gate = IdempotencyLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await _context.OrderNotifications.SingleOrDefaultAsync(x =>
                x.ResendOfNotificationId == notificationId && x.IdempotencyKey == idempotencyKey,
                cancellationToken);
            if (existing is not null) return (existing, null);

            var original = await _context.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId,
                cancellationToken);
            if (original is null) return (null, "not-found");

            await RefreshAsync(new[] { original }, cancellationToken);
            if (!ResendableStatuses.Contains(original.ProviderStatus, StringComparer.OrdinalIgnoreCase))
                return (null, "not-resendable");
            if (original.Body is null) return (null, "content-disposed");

            var order = await _context.Orders.SingleOrDefaultAsync(x => x.Id == original.OrderId,
                cancellationToken);
            if (order?.Status == OrderStatus.Cancelled && original.Kind == DeliveryFollowUp)
                return (null, "order-cancelled");

            var contact = await _context.ContactNumbers.SingleOrDefaultAsync(x =>
                x.Id == original.ContactNumberId && x.BuyerId == original.BuyerId, cancellationToken);
            if (contact is null) return (null, "contact-removed");

            var resend = new OrderNotification(original.OrderId, original.BuyerId,
                original.ContactNumberId, original.Kind, original.Body, DateTimeOffset.UtcNow,
                resendOfNotificationId: original.Id, idempotencyKey: idempotencyKey);
            _context.OrderNotifications.Add(resend);
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                _context.Entry(resend).State = EntityState.Detached;
                existing = await _context.OrderNotifications.SingleOrDefaultAsync(x =>
                    x.ResendOfNotificationId == notificationId && x.IdempotencyKey == idempotencyKey,
                    cancellationToken);
                if (existing is not null) return (existing, null);
                throw;
            }

            await TrySendAsync(resend, contact.PhoneNumber, cancellationToken);
            return (resend, null);
        }
        finally
        {
            gate.Release();
            IdempotencyLocks.TryRemove(lockKey, out _);
        }
    }

    public async Task<bool> DisposeContentAsync(OrderNotification notification,
        CancellationToken cancellationToken = default)
    {
        if (notification.ContentDisposedAt.HasValue) return true;
        if (notification.ProviderMessageId is not null)
        {
            try
            {
                var state = await _provider.RedactAsync(notification.ProviderMessageId, cancellationToken);
                Apply(notification, state);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                _logger.LogWarning("Provider content disposal failed for notification {NotificationId}.",
                    notification.Id);
                return false;
            }
        }
        notification.DisposeContent(DateTimeOffset.UtcNow);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public Task<IReadOnlyCollection<ProviderMessage>> ListProviderMessagesAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken = default) =>
        _provider.ListApplicationMessagesAsync(from, to, cancellationToken);

    private async Task TrySendAsync(OrderNotification notification, string destination,
        CancellationToken cancellationToken)
    {
        try
        {
            var state = await _provider.SendAsync(destination, notification.Body!,
                notification.ScheduledFor, cancellationToken);
            Apply(notification, state);
        }
        catch (MessageProviderException exception)
        {
            notification.MarkProviderFailure(exception.ProviderErrorCode);
            _logger.LogWarning("Provider send failed for notification {NotificationId}.", notification.Id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            notification.MarkProviderFailure(null);
        }
        catch
        {
            notification.MarkProviderFailure(null);
            _logger.LogWarning("Provider send failed for notification {NotificationId}.", notification.Id);
        }

        try
        {
            await _context.SaveChangesAsync(CancellationToken.None);
        }
        catch
        {
            _logger.LogError("Could not persist provider state for notification {NotificationId}.",
                notification.Id);
        }
    }

    private async Task<bool> TryCancelScheduledAsync(OrderNotification notification,
        CancellationToken cancellationToken)
    {
        try
        {
            var state = await _provider.GetAsync(notification.ProviderMessageId!, cancellationToken);
            if (state.Status.Equals("scheduled", StringComparison.OrdinalIgnoreCase))
                state = await _provider.CancelAsync(notification.ProviderMessageId!, cancellationToken);
            Apply(notification, state);
            await _context.SaveChangesAsync(cancellationToken);
            return !state.Status.Equals("scheduled", StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            notification.MarkCancellationPending();
            await _context.SaveChangesAsync(CancellationToken.None);
            _logger.LogWarning("Provider cancellation will be retried for notification {NotificationId}.",
                notification.Id);
            return false;
        }
    }

    private static void Apply(OrderNotification notification, ProviderMessage state) =>
        notification.ApplyProviderState(state.Id, state.Status, state.ErrorCode,
            state.DateCreated, state.DateSent);
}
