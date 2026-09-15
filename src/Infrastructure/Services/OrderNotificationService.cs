using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public sealed class OrderNotificationService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ResendLocks = new();
    private readonly CatalogContext _db;
    private readonly ITwilioMessagingGateway _provider;
    private readonly TimeProvider _clock;

    public OrderNotificationService(CatalogContext db, ITwilioMessagingGateway provider, TimeProvider clock)
    {
        _db = db;
        _provider = provider;
        _clock = clock;
    }

    public async Task<ContactRegistrationResult> RegisterContactAsync(string buyerId, string number,
        string? countryCode, CancellationToken cancellationToken)
    {
        var validation = await _provider.ValidatePhoneNumberAsync(number, countryCode, cancellationToken);
        if (!validation.Valid || validation.E164Number is null)
            return new(false, null, validation.Errors);

        var contact = await _db.ContactNumbers.SingleOrDefaultAsync(
            x => x.BuyerId == buyerId && x.E164Number == validation.E164Number, cancellationToken);
        var created = contact is null;
        if (contact is null)
        {
            contact = new ContactNumber(buyerId, validation.E164Number, Now());
            _db.ContactNumbers.Add(contact);
        }
        else if (contact.DeletedAt.HasValue)
        {
            contact.Restore(Now());
        }
        await _db.SaveChangesAsync(cancellationToken);
        return new(true, contact, Array.Empty<string>(), created);
    }

    public Task<List<ContactNumber>> GetContactsAsync(string buyerId, CancellationToken cancellationToken) =>
        _db.ContactNumbers.AsNoTracking().Where(x => x.BuyerId == buyerId && x.DeletedAt == null)
            .OrderBy(x => x.Id).ToListAsync(cancellationToken);

    public async Task<bool> DeleteContactAsync(string buyerId, int id, CancellationToken cancellationToken)
    {
        var contact = await _db.ContactNumbers.SingleOrDefaultAsync(
            x => x.Id == id && x.BuyerId == buyerId && x.DeletedAt == null, cancellationToken);
        if (contact is null) return false;
        contact.Remove(Now());
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines,
        Address address, CancellationToken cancellationToken)
    {
        if (lines.Count == 0 || lines.Any(x => x.Quantity <= 0))
            throw new RequestValidationException("At least one catalog item with a positive quantity is required.");

        var grouped = lines.GroupBy(x => x.CatalogItemId)
            .Select(x => new OrderLineInput(x.Key, x.Sum(y => y.Quantity))).ToList();
        var ids = grouped.Select(x => x.CatalogItemId).ToList();
        var catalogItems = await _db.CatalogItems.Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
        if (catalogItems.Count != ids.Distinct().Count())
            throw new RequestValidationException("One or more catalog items do not exist.");

        var orderItems = grouped.Select(line =>
        {
            var item = catalogItems.Single(x => x.Id == line.CatalogItemId);
            return new OrderItem(new CatalogItemOrdered(item.Id, item.Name, item.PictureUri), item.Price, line.Quantity);
        }).ToList();
        var order = new Order(buyerId, address, orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        await NotifyActiveContactsAsync(order, NotificationKind.OrderPlaced,
            $"Your eShop order #{order.Id} has been placed.", null, cancellationToken);
        return order;
    }

    public async Task<Order?> DispatchAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null) return null;
        order.Dispatch(Now());
        await _db.SaveChangesAsync(cancellationToken);
        await NotifyActiveContactsAsync(order, NotificationKind.OrderDispatched,
            $"Your eShop order #{order.Id} has been dispatched and is on its way.", null, cancellationToken);
        await NotifyActiveContactsAsync(order, NotificationKind.DeliveryFollowUp,
            $"How did delivery of your eShop order #{order.Id} go?", Now().AddDays(3), cancellationToken);
        return order;
    }

    public async Task<Order?> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null) return null;
        var wasAlreadyCancelled = order.Status == OrderStatus.Cancelled;

        var followUps = await _db.OrderNotifications.Where(x => x.OrderId == orderId &&
            x.Kind == NotificationKind.DeliveryFollowUp && x.ProviderMessageSid != null).ToListAsync(cancellationToken);
        foreach (var followUp in followUps)
        {
            var current = await _provider.FetchAsync(followUp.ProviderMessageSid!, cancellationToken);
            ApplyProviderState(followUp, current);
            if (string.Equals(current.Status, "scheduled", StringComparison.OrdinalIgnoreCase))
            {
                var cancelled = await CancelScheduledMessageWithRetryAsync(followUp.ProviderMessageSid!, cancellationToken);
                ApplyProviderState(followUp, cancelled);
            }
        }
        // Do not commit the cancelled order state until every still-scheduled provider
        // message is confirmed cancelled. This prevents a cancelled order from retaining
        // a live delivery follow-up if the provider is temporarily unavailable.
        if (!wasAlreadyCancelled)
            order.Cancel(Now());
        await _db.SaveChangesAsync(cancellationToken);
        var cancellationAlreadyNotified = await _db.OrderNotifications.AnyAsync(x =>
            x.OrderId == orderId && x.Kind == NotificationKind.OrderCancelled, cancellationToken);
        if (!cancellationAlreadyNotified)
            await NotifyActiveContactsAsync(order, NotificationKind.OrderCancelled,
                $"Your eShop order #{order.Id} has been cancelled.", null, cancellationToken);
        return order;
    }

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(string buyerId, int orderId,
        CancellationToken cancellationToken)
    {
        if (!await _db.Orders.AnyAsync(x => x.Id == orderId && x.BuyerId == buyerId, cancellationToken))
            throw new ResourceNotFoundException();
        var notifications = await _db.OrderNotifications.Where(x => x.OrderId == orderId)
            .OrderBy(x => x.Id).ToListAsync(cancellationToken);
        await RefreshAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<IReadOnlyList<OrderSummary>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _db.Orders.AsNoTracking().Where(x => x.BuyerId == buyerId)
            .Include(x => x.OrderItems).OrderByDescending(x => x.OrderDate).ToListAsync(cancellationToken);
        var orderIds = orders.Select(x => x.Id).ToList();
        var notifications = await _db.OrderNotifications.Where(x => orderIds.Contains(x.OrderId)).ToListAsync(cancellationToken);
        await RefreshAsync(notifications, cancellationToken);
        return orders.Select(x => new OrderSummary(x, notifications.Where(n => n.OrderId == x.Id).OrderBy(n => n.Id).ToList())).ToList();
    }

    public async Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128)
            throw new RequestValidationException("An idempotency key of at most 128 characters is required.");

        var lockKey = $"{notificationId}:{idempotencyKey}";
        var gate = ResendLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await _db.OrderNotifications.SingleOrDefaultAsync(x =>
                x.SourceNotificationId == notificationId && x.IdempotencyKey == idempotencyKey, cancellationToken);
            if (existing is not null) return existing;

            var source = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken);
            if (source is null) return null;
            if (source.ProviderMessageSid is not null)
            {
                try { ApplyProviderState(source, await _provider.FetchAsync(source.ProviderMessageSid, cancellationToken)); }
                catch (TwilioProviderException) { }
            }
            if (source.Content is null)
                throw new RequestValidationException("A notification whose content was disposed cannot be resent.");
            if (!IsFailed(source.ProviderStatus))
                throw new RequestValidationException("Only a failed or undelivered notification can be resent.");
            var activeContact = await _db.ContactNumbers.SingleOrDefaultAsync(x =>
                x.Id == source.ContactNumberId && x.DeletedAt == null, cancellationToken);
            if (activeContact is null)
                throw new RequestValidationException("The destination contact number has been removed.");

            var resend = new OrderNotification(source.OrderId, source.ContactNumberId, NotificationKind.Resend,
                source.Content, Now(), source.Id, idempotencyKey);
            _db.OrderNotifications.Add(resend);
            await _db.SaveChangesAsync(cancellationToken);
            await TrySendAsync(resend, activeContact.E164Number, null, cancellationToken);
            return resend;
        }
        finally
        {
            gate.Release();
            ResendLocks.TryRemove(lockKey, out _);
        }
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken);
        if (notification is null) return false;
        if (notification.ContentDisposedAt.HasValue) return true;
        if (notification.ProviderMessageSid is not null)
        {
            var redacted = await _provider.RedactAsync(notification.ProviderMessageSid, cancellationToken);
            ApplyProviderState(notification, redacted);
            var confirmation = await _provider.FetchAsync(notification.ProviderMessageSid, cancellationToken);
            if (!string.IsNullOrEmpty(confirmation.Body))
                throw new TwilioProviderException("Provider content disposal could not be confirmed.");
            ApplyProviderState(notification, confirmation);
        }
        notification.DisposeContent(Now());
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<ReconciliationResult> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from > to) throw new RequestValidationException("from must be earlier than or equal to to.");
        var providerMessages = await _provider.ListAsync(from, to, cancellationToken);
        var sids = providerMessages.Select(x => x.Sid).ToList();
        var local = await _db.OrderNotifications.AsNoTracking()
            .Where(x => x.ProviderMessageSid != null &&
                ((x.ProviderDateSent >= from && x.ProviderDateSent <= to) ||
                 (x.ProviderDateSent == null && x.ProviderDateCreated >= from && x.ProviderDateCreated <= to) ||
                 (x.ProviderDateSent == null && x.ProviderDateCreated == null && x.CreatedAt >= from && x.CreatedAt <= to)))
            .ToListAsync(cancellationToken);
        // Include local records matching the provider page even if their cached timestamp is stale.
        var matching = await _db.OrderNotifications.AsNoTracking()
            .Where(x => x.ProviderMessageSid != null && sids.Contains(x.ProviderMessageSid)).ToListAsync(cancellationToken);
        local = local.Concat(matching).DistinctBy(x => x.Id).ToList();

        var localBySid = local.Where(x => x.ProviderMessageSid is not null).ToDictionary(x => x.ProviderMessageSid!);
        var providerBySid = providerMessages.ToDictionary(x => x.Sid);
        var rows = providerMessages.Select(p => new ReconciliationRow(p.Sid,
                localBySid.TryGetValue(p.Sid, out var n) ? n.Id : null, p.Status,
                n?.ProviderStatus, true, n is not null))
            .Concat(local.Where(n => n.ProviderMessageSid is not null && !providerBySid.ContainsKey(n.ProviderMessageSid))
                .Select(n => new ReconciliationRow(n.ProviderMessageSid!, n.Id, null, n.ProviderStatus, false, true)))
            .OrderBy(x => x.ProviderMessageSid).ToList();
        return new(from, to, rows);
    }

    private async Task NotifyActiveContactsAsync(Order order, NotificationKind kind, string content,
        DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        var contacts = await _db.ContactNumbers.Where(x => x.BuyerId == order.BuyerId && x.DeletedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var contact in contacts)
        {
            var notification = new OrderNotification(order.Id, contact.Id, kind, content, Now());
            _db.OrderNotifications.Add(notification);
            await _db.SaveChangesAsync(cancellationToken);
            await TrySendAsync(notification, contact.E164Number, sendAt, cancellationToken);
        }
    }

    private async Task TrySendAsync(OrderNotification notification, string destination, DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var providerMessage = await _provider.SendAsync(destination, notification.Content!, sendAt, cancellationToken);
            notification.RecordProviderState(providerMessage.Sid, providerMessage.Status, providerMessage.ErrorCode,
                providerMessage.DateCreated, providerMessage.DateSent, sendAt);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            notification.RecordSendFailure();
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task RefreshAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        var changed = false;
        foreach (var notification in notifications.Where(x => x.ProviderMessageSid is not null))
        {
            try
            {
                ApplyProviderState(notification, await _provider.FetchAsync(notification.ProviderMessageSid!, cancellationToken));
                changed = true;
            }
            catch (TwilioProviderException) { }
        }
        if (changed) await _db.SaveChangesAsync(cancellationToken);
    }

    private static void ApplyProviderState(OrderNotification notification, ProviderMessage providerMessage) =>
        notification.RecordProviderState(providerMessage.Sid, providerMessage.Status, providerMessage.ErrorCode,
            providerMessage.DateCreated, providerMessage.DateSent, notification.ScheduledFor);

    private static bool IsFailed(string status) => status.Equals("failed", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("undelivered", StringComparison.OrdinalIgnoreCase) || status.Equals("send_failed", StringComparison.OrdinalIgnoreCase);

    private async Task<ProviderMessage> CancelScheduledMessageWithRetryAsync(string messageSid, CancellationToken cancellationToken)
    {
        TwilioProviderException? lastError = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try { return await _provider.CancelAsync(messageSid, cancellationToken); }
            catch (TwilioProviderException ex) when (attempt < 3)
            {
                lastError = ex;
                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
            }
        }
        throw lastError ?? new TwilioProviderException("Provider follow-up cancellation could not be confirmed.");
    }

    private DateTimeOffset Now() => _clock.GetUtcNow();
}

public sealed record OrderLineInput(int CatalogItemId, int Quantity);
public sealed record ContactRegistrationResult(bool Valid, ContactNumber? Contact, IReadOnlyList<string> Errors, bool Created = false);
public sealed record OrderSummary(Order Order, IReadOnlyList<OrderNotification> Notifications);
public sealed record ReconciliationResult(DateTimeOffset From, DateTimeOffset To, IReadOnlyList<ReconciliationRow> Messages);
public sealed record ReconciliationRow(string ProviderMessageSid, int? NotificationId, string? ProviderStatus,
    string? LocalStatus, bool ProviderHasRecord, bool EshopHasRecord);
public sealed class RequestValidationException : Exception { public RequestValidationException(string message) : base(message) { } }
public sealed class ResourceNotFoundException : Exception { }
