using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public sealed class OrderNotificationService
{
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ResendLocks = new(StringComparer.Ordinal);

    private readonly CatalogContext _db;
    private readonly ITwilioMessagingGateway _twilio;
    private readonly ILogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        CatalogContext db,
        ITwilioMessagingGateway twilio,
        ILogger<OrderNotificationService> logger)
    {
        _db = db;
        _twilio = twilio;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterContactAsync(string buyerId, string suppliedNumber, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(suppliedNumber)) throw new InvalidContactNumberException();
        var canonical = await _twilio.ValidateAndCanonicalizeAsync(suppliedNumber, ct);

        var existing = await _db.ContactNumbers.SingleOrDefaultAsync(
            x => x.BuyerId == buyerId && x.CanonicalNumber == canonical, ct);
        if (existing is not null) return existing;

        var contact = new ContactNumber(buyerId, canonical, DateTimeOffset.UtcNow);
        _db.ContactNumbers.Add(contact);
        await _db.SaveChangesAsync(ct);
        return contact;
    }

    public Task<List<ContactNumber>> GetContactsAsync(string buyerId, CancellationToken ct) =>
        _db.ContactNumbers.AsNoTracking()
            .Where(x => x.BuyerId == buyerId)
            .OrderBy(x => x.Id)
            .ToListAsync(ct);

    public async Task<bool> DeleteContactAsync(string buyerId, int contactNumberId, CancellationToken ct)
    {
        var contact = await _db.ContactNumbers.SingleOrDefaultAsync(
            x => x.Id == contactNumberId && x.BuyerId == buyerId, ct);
        if (contact is null) return false;

        var scheduled = await _db.OrderNotifications
            .Where(x => x.ContactNumberId == contactNumberId &&
                        x.Kind == NotificationKind.DeliveryFollowUp &&
                        x.ProviderMessageId != null &&
                        x.ProviderStatus != "canceled" &&
                        x.ProviderStatus != "delivered" &&
                        x.ProviderStatus != "sent")
            .ToListAsync(ct);

        foreach (var notification in scheduled)
        {
            await TryCancelScheduledAsync(notification, ct);
        }

        _db.ContactNumbers.Remove(contact);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, PlaceOrderRequest request, CancellationToken ct)
    {
        if (request.Items is null || request.Items.Count == 0 || request.Items.Any(x => x.Quantity is < 1 or > 1000))
            throw new ArgumentException("At least one catalog item with a quantity from 1 to 1000 is required.");

        var requested = request.Items
            .GroupBy(x => x.CatalogItemId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity));
        if (requested.Keys.Any(x => x <= 0) || requested.Values.Any(x => x > 1000))
            throw new ArgumentException("Catalog item identifiers and combined quantities must be valid.");

        var catalogItems = await _db.CatalogItems
            .Where(x => requested.Keys.Contains(x.Id))
            .ToListAsync(ct);
        if (catalogItems.Count != requested.Count)
            throw new KeyNotFoundException("One or more catalog items do not exist.");

        var items = catalogItems.Select(x => new OrderItem(
            new CatalogItemOrdered(x.Id, x.Name, x.PictureUri),
            x.Price,
            requested[x.Id])).ToList();
        var address = new Address("Not supplied", "Not supplied", string.Empty, "Not supplied", "N/A");
        var order = new Order(buyerId, address, items);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);

        await NotifyAllActiveContactsAsync(
            order,
            NotificationKind.OrderPlaced,
            $"Your eShop order #{order.Id} has been placed.",
            scheduledFor: null,
            ct);
        return order;
    }

    public async Task<(Order? Order, bool Changed)> DispatchAsync(int orderId, CancellationToken ct)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, ct);
        if (order is null) return (null, false);

        var changed = order.Dispatch(DateTimeOffset.UtcNow);
        if (!changed) return (order, false);
        await _db.SaveChangesAsync(ct);

        await NotifyAllActiveContactsAsync(
            order,
            NotificationKind.OrderDispatched,
            $"Your eShop order #{order.Id} is on its way.",
            scheduledFor: null,
            ct);

        var followUpAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        await NotifyAllActiveContactsAsync(
            order,
            NotificationKind.DeliveryFollowUp,
            $"How did delivery of eShop order #{order.Id} go?",
            followUpAt,
            ct);
        return (order, true);
    }

    public async Task<(Order? Order, bool Changed)> CancelAsync(int orderId, CancellationToken ct)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, ct);
        if (order is null) return (null, false);

        var changed = order.Cancel(DateTimeOffset.UtcNow);
        if (!changed) return (order, false);
        await _db.SaveChangesAsync(ct);

        var followUps = await _db.OrderNotifications
            .Where(x => x.OrderId == orderId &&
                        x.Kind == NotificationKind.DeliveryFollowUp &&
                        x.ProviderMessageId != null &&
                        x.ProviderStatus != "canceled" &&
                        x.ProviderStatus != "delivered" &&
                        x.ProviderStatus != "sent")
            .ToListAsync(ct);
        foreach (var followUp in followUps)
        {
            await TryCancelScheduledAsync(followUp, ct);
        }

        await NotifyAllActiveContactsAsync(
            order,
            NotificationKind.OrderCancelled,
            $"Your eShop order #{order.Id} has been cancelled.",
            scheduledFor: null,
            ct);
        return (order, true);
    }

    public async Task<List<MyOrderResponse>> GetMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        var orders = await _db.Orders.AsNoTracking()
            .Include(x => x.OrderItems)
            .Where(x => x.BuyerId == buyerId)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(ct);
        var orderIds = orders.Select(x => x.Id).ToArray();
        var notifications = await _db.OrderNotifications
            .Where(x => orderIds.Contains(x.OrderId))
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);
        await RefreshBestEffortAsync(notifications, ct);

        return orders.Select(order => new MyOrderResponse(
            order.Id,
            order.OrderDate,
            order.Status.ToString(),
            order.Total(),
            notifications.Where(x => x.OrderId == order.Id).Select(ToResponse).ToList())).ToList();
    }

    public async Task<List<NotificationResponse>?> GetOrderNotificationsAsync(string buyerId, int orderId, CancellationToken ct)
    {
        if (!await _db.Orders.AnyAsync(x => x.Id == orderId && x.BuyerId == buyerId, ct)) return null;
        var notifications = await _db.OrderNotifications
            .Where(x => x.OrderId == orderId && x.BuyerId == buyerId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);
        await RefreshBestEffortAsync(notifications, ct);
        return notifications.Select(ToResponse).ToList();
    }

    public async Task<int?> ResendAsync(int originalNotificationId, string idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128)
            throw new ArgumentException("An idempotency key of at most 128 characters is required.");

        var lockKey = $"{originalNotificationId}:{idempotencyKey}";
        var gate = ResendLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var prior = await _db.OrderNotifications.SingleOrDefaultAsync(
                x => x.OriginalNotificationId == originalNotificationId && x.IdempotencyKey == idempotencyKey, ct);
            if (prior is not null) return prior.Id;

            var original = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == originalNotificationId, ct);
            if (original is null) return null;
            if (!DidNotReach(original)) throw new NotificationConflictException("Only a notification that did not reach the shopper can be resent.");
            if (original.Content is null) throw new NotificationConflictException("Disposed message content cannot be resent.");

            var contact = await _db.ContactNumbers.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == original.ContactNumberId && x.BuyerId == original.BuyerId, ct);
            if (contact is null) throw new NotificationConflictException("The destination is no longer registered.");

            var resend = new OrderNotification(
                original.OrderId,
                original.BuyerId,
                original.ContactNumberId,
                NotificationKind.Resend,
                original.Content,
                DateTimeOffset.UtcNow,
                originalNotificationId: original.Id,
                idempotencyKey: idempotencyKey);
            _db.OrderNotifications.Add(resend);
            await _db.SaveChangesAsync(ct);
            await SendBestEffortAsync(resend, contact.CanonicalNumber, ct);
            return resend.Id;
        }
        finally
        {
            gate.Release();
            if (gate.CurrentCount == 1) ResendLocks.TryRemove(lockKey, out _);
        }
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken ct)
    {
        var notification = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, ct);
        if (notification is null) return false;
        if (notification.ContentDisposedAt.HasValue) return true;

        if (notification.ProviderMessageId is not null)
        {
            var state = await _twilio.RedactContentAsync(notification.ProviderMessageId, ct);
            ApplyProviderState(notification, state);
        }

        notification.DisposeContent(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (from >= to) throw new ArgumentException("The 'from' date-time must be earlier than 'to'.");
        var provider = await _twilio.ListAsync(from, to, ct);
        var local = await _db.OrderNotifications.AsNoTracking()
            .Where(x => x.CreatedAt > from && x.CreatedAt < to && x.ProviderFrom == _twilio.ConfiguredFromNumber)
            .ToListAsync(ct);

        var localBySid = local.Where(x => x.ProviderMessageId != null)
            .ToDictionary(x => x.ProviderMessageId!, StringComparer.Ordinal);
        var providerBySid = provider.ToDictionary(x => x.Sid, StringComparer.Ordinal);
        var entries = new List<ReconciliationEntry>();

        foreach (var message in provider.OrderBy(x => x.DateSent))
        {
            localBySid.TryGetValue(message.Sid, out var notification);
            entries.Add(new ReconciliationEntry(
                notification is null ? "provider-only" : "matched",
                notification?.Id,
                message.Sid,
                message.Status,
                message.DateSent));
        }

        entries.AddRange(local
            .Where(x => x.ProviderMessageId is null || !providerBySid.ContainsKey(x.ProviderMessageId))
            .Select(x => new ReconciliationEntry(
                "local-only",
                x.Id,
                x.ProviderMessageId,
                x.ProviderStatus,
                x.ProviderDateSent)));

        return new ReconciliationResponse(
            from,
            to,
            "Strict provider bounds: DateSent > from and DateSent < to.",
            provider.Count,
            local.Count,
            entries);
    }

    public async Task RetryPendingCancellationsAsync(CancellationToken ct)
    {
        var pending = await _db.OrderNotifications
            .Where(x => x.CancellationPending && x.ProviderMessageId != null)
            .Take(100)
            .ToListAsync(ct);
        foreach (var notification in pending)
        {
            await TryCancelScheduledAsync(notification, ct);
        }
    }

    private async Task NotifyAllActiveContactsAsync(
        Order order,
        NotificationKind kind,
        string content,
        DateTimeOffset? scheduledFor,
        CancellationToken ct)
    {
        var contacts = await _db.ContactNumbers.AsNoTracking()
            .Where(x => x.BuyerId == order.BuyerId)
            .OrderBy(x => x.Id)
            .ToListAsync(ct);
        foreach (var contact in contacts)
        {
            var notification = new OrderNotification(
                order.Id,
                order.BuyerId,
                contact.Id,
                kind,
                content,
                DateTimeOffset.UtcNow,
                scheduledFor);
            _db.OrderNotifications.Add(notification);
            await _db.SaveChangesAsync(ct);
            await SendBestEffortAsync(notification, contact.CanonicalNumber, ct);
        }
    }

    private async Task SendBestEffortAsync(OrderNotification notification, string canonicalNumber, CancellationToken ct)
    {
        try
        {
            var state = await _twilio.SendAsync(canonicalNumber, notification.Content!, notification.ScheduledFor, ct);
            ApplyProviderState(notification, state);
        }
        catch (TwilioProviderException ex)
        {
            notification.RecordProviderFailure(ex.StatusCode is null ? null : (int)ex.StatusCode, DateTimeOffset.UtcNow);
            _logger.LogWarning(
                "Twilio notification attempt failed for notification {NotificationId}, order {OrderId}, provider HTTP status {ProviderStatus}.",
                notification.Id,
                notification.OrderId,
                ex.StatusCode is null ? null : (int)ex.StatusCode);
        }
        finally
        {
            await _db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private async Task TryCancelScheduledAsync(OrderNotification notification, CancellationToken ct)
    {
        try
        {
            var state = await _twilio.CancelScheduledAsync(notification.ProviderMessageId!, ct);
            ApplyProviderState(notification, state);
        }
        catch (TwilioProviderException ex)
        {
            notification.MarkCancellationPending();
            _logger.LogWarning(
                "Twilio scheduled-message cancellation is pending for notification {NotificationId}, order {OrderId}, provider HTTP status {ProviderStatus}.",
                notification.Id,
                notification.OrderId,
                ex.StatusCode is null ? null : (int)ex.StatusCode);
        }
        finally
        {
            await _db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private async Task RefreshBestEffortAsync(List<OrderNotification> notifications, CancellationToken ct)
    {
        foreach (var notification in notifications.Where(x => x.ProviderMessageId is not null))
        {
            try
            {
                ApplyProviderState(notification, await _twilio.FetchAsync(notification.ProviderMessageId!, ct));
            }
            catch (TwilioProviderException ex)
            {
                _logger.LogWarning(
                    "Twilio status refresh failed for notification {NotificationId}, provider HTTP status {ProviderStatus}.",
                    notification.Id,
                    ex.StatusCode is null ? null : (int)ex.StatusCode);
            }
        }
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private void ApplyProviderState(OrderNotification notification, ProviderMessage state) =>
        notification.RecordProviderState(
            state.Sid,
            state.Status,
            state.From ?? _twilio.ConfiguredFromNumber,
            state.ErrorCode,
            state.ErrorMessage,
            state.DateCreated,
            state.DateUpdated,
            state.DateSent,
            DateTimeOffset.UtcNow);

    private static bool DidNotReach(OrderNotification notification) =>
        notification.LocalOutcome == NotificationLocalOutcome.ProviderCallFailed ||
        notification.ProviderErrorCode.HasValue ||
        notification.ProviderStatus is "failed" or "undelivered" or "partially_delivered";

    private static NotificationResponse ToResponse(OrderNotification x) => new(
        x.Id,
        x.OrderId,
        x.Kind.ToString(),
        x.Content,
        x.ContentDisposedAt.HasValue,
        x.LocalOutcome.ToString(),
        x.ProviderMessageId,
        x.ProviderStatus,
        x.ProviderErrorCode,
        x.CreatedAt,
        x.ScheduledFor,
        x.LastProviderSyncAt,
        x.ProviderMessageId is not null && (!x.LastProviderSyncAt.HasValue || x.LastProviderSyncAt < DateTimeOffset.UtcNow.AddMinutes(-5)),
        x.OriginalNotificationId);
}
