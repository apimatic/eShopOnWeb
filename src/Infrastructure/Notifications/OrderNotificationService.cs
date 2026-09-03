using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

public sealed class OrderNotificationService : IOrderNotificationService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> ResendLocks = new();
    private static readonly TimeSpan ProviderBudget = TimeSpan.FromSeconds(30);
    private readonly CatalogContext _db;
    private readonly ITwilioMessagingGateway _provider;
    private readonly ILogger<OrderNotificationService> _logger;

    public OrderNotificationService(CatalogContext db, ITwilioMessagingGateway provider,
        ILogger<OrderNotificationService> logger)
    {
        _db = db;
        _provider = provider;
        _logger = logger;
    }

    public async Task<int> RegisterContactNumberAsync(string shopperId, string number, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(number))
            throw new ContactNumberValidationException("A mobile number is required.");

        using var deadline = Deadline(ct);
        string canonical;
        try
        {
            canonical = await _provider.ValidateAndCanonicalizeAsync(number, deadline.Token);
        }
        catch (TwilioProviderException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity)
        {
            throw new ContactNumberValidationException("The provider does not consider this a usable destination.");
        }

        var existing = await _db.ContactNumbers.SingleOrDefaultAsync(
            x => x.ShopperId == shopperId && x.CanonicalNumber == canonical, ct);
        if (existing is not null) return existing.Id;

        var contact = new ContactNumber(shopperId, canonical);
        _db.ContactNumbers.Add(contact);
        await _db.SaveChangesAsync(ct);
        return contact.Id;
    }

    public async Task<IReadOnlyList<ContactNumberView>> GetContactNumbersAsync(string shopperId, CancellationToken ct) =>
        await _db.ContactNumbers.AsNoTracking().Where(x => x.ShopperId == shopperId)
            .OrderBy(x => x.Id).Select(x => new ContactNumberView(x.Id, x.CanonicalNumber)).ToListAsync(ct);

    public async Task<bool> DeleteContactNumberAsync(string shopperId, int contactNumberId, CancellationToken ct)
    {
        var contact = await _db.ContactNumbers.SingleOrDefaultAsync(
            x => x.Id == contactNumberId && x.ShopperId == shopperId, ct);
        if (contact is null) return false;
        _db.ContactNumbers.Remove(contact);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> PlaceOrderAsync(string shopperId, Address shippingAddress,
        IReadOnlyList<OrderLineRequest> lines, CancellationToken ct)
    {
        var normalized = NormalizeLines(lines);
        var ids = normalized.Keys.ToArray();
        var catalogItems = await _db.CatalogItems.Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        if (catalogItems.Count != ids.Length)
            throw new NotificationOperationException("One or more catalog items do not exist.");

        var orderItems = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, item.PictureUri), item.Price, normalized[item.Id])).ToList();
        var order = new Order(shopperId, shippingAddress, orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);

        using var deadline = Deadline(ct);
        await NotifyAllAsync(order, NotificationKind.OrderPlaced,
            $"Your eShop order #{order.Id} has been placed.", null, deadline.Token);
        return order.Id;
    }

    public async Task<bool> DispatchOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, ct);
        if (order is null) return false;
        try { order.Dispatch(); }
        catch (InvalidOperationException ex) { throw new NotificationOperationException(ex.Message); }
        await _db.SaveChangesAsync(ct);

        using var deadline = Deadline(ct);
        await NotifyAllAsync(order, NotificationKind.OrderDispatched,
            $"Your eShop order #{order.Id} is on its way.", null, deadline.Token);
        var followUpAt = DateTimeOffset.UtcNow.AddDays(3);
        await NotifyAllAsync(order, NotificationKind.DeliveryFollowUp,
            $"How did delivery of your eShop order #{order.Id} go?", followUpAt, deadline.Token);
        return true;
    }

    public async Task<bool> CancelOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, ct);
        if (order is null) return false;
        try { order.Cancel(); }
        catch (InvalidOperationException ex) { throw new NotificationOperationException(ex.Message); }
        await _db.SaveChangesAsync(ct);

        using var deadline = Deadline(ct);
        var followUps = await _db.OrderNotifications.Where(x => x.OrderId == orderId &&
            x.Kind == NotificationKind.DeliveryFollowUp && x.ProviderMessageSid != null).ToListAsync(ct);
        foreach (var followUp in followUps)
        {
            if (IsTerminal(followUp.DeliveryStatus)) continue;
            try
            {
                var state = await _provider.CancelAsync(followUp.ProviderMessageSid!, deadline.Token);
                Apply(followUp, state);
            }
            catch (Exception)
            {
                followUp.RecordProviderFailure("cancel-request-failed");
                _logger.LogWarning("Provider cancellation failed for NotificationId {NotificationId} on OrderId {OrderId}.", followUp.Id, orderId);
            }
        }
        await _db.SaveChangesAsync(CancellationToken.None);

        await NotifyAllAsync(order, NotificationKind.OrderCancelled,
            $"Your eShop order #{order.Id} has been cancelled.", null, deadline.Token);
        return true;
    }

    public async Task<IReadOnlyList<OrderView>> GetMyOrdersAsync(string shopperId, CancellationToken ct)
    {
        var orders = await _db.Orders.AsNoTracking().Include(x => x.OrderItems)
            .Where(x => x.BuyerId == shopperId).OrderByDescending(x => x.OrderDate).ToListAsync(ct);
        using var deadline = Deadline(ct);
        var result = new List<OrderView>(orders.Count);
        foreach (var order in orders)
        {
            var notifications = await LoadAndRefreshAsync(shopperId, order.Id, deadline.Token);
            result.Add(new OrderView(order.Id, order.OrderDate, order.Status.ToString(), order.Total(), notifications));
        }
        return result;
    }

    public async Task<IReadOnlyList<NotificationView>?> GetOrderNotificationsAsync(string shopperId, int orderId, CancellationToken ct)
    {
        var owned = await _db.Orders.AsNoTracking().AnyAsync(x => x.Id == orderId && x.BuyerId == shopperId, ct);
        if (!owned) return null;
        using var deadline = Deadline(ct);
        return await LoadAndRefreshAsync(shopperId, orderId, deadline.Token);
    }

    public async Task<int?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
            throw new NotificationOperationException("An idempotency key of 1 to 200 characters is required.");

        var gate = ResendLocks.GetOrAdd(notificationId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var existing = await _db.OrderNotifications.SingleOrDefaultAsync(x =>
                x.ResendOfNotificationId == notificationId && x.IdempotencyKey == idempotencyKey, ct);
            if (existing is not null) return existing.Id;

            var source = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, ct);
            if (source is null) return null;
            if (source.Content is null)
                throw new NotificationOperationException("A disposed message cannot be resent.");

            using var deadline = Deadline(ct);
            await RefreshAsync(source, deadline.Token);
            if (!CanResend(source.DeliveryStatus))
                throw new NotificationOperationException("Only a message that did not reach the shopper can be resent.");

            var contact = await _db.ContactNumbers.AsNoTracking().SingleOrDefaultAsync(
                x => x.Id == source.ContactNumberId && x.ShopperId == source.ShopperId, ct);
            if (contact is null)
                throw new NotificationOperationException("The original contact number is no longer registered.");

            var resend = new OrderNotification(source.OrderId, source.ShopperId, contact.Id,
                source.Kind, source.Content, null, source.Id, idempotencyKey);
            _db.OrderNotifications.Add(resend);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                _db.Entry(resend).State = EntityState.Detached;
                var winner = await _db.OrderNotifications.AsNoTracking().SingleOrDefaultAsync(x =>
                    x.ResendOfNotificationId == notificationId && x.IdempotencyKey == idempotencyKey, ct);
                if (winner is not null) return winner.Id;
                throw;
            }
            await SendPersistedAsync(resend, contact.CanonicalNumber, deadline.Token);
            return resend.Id;
        }
        finally { gate.Release(); }
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken ct)
    {
        var notification = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, ct);
        if (notification is null) return false;
        if (notification.Content is null) return true;

        if (notification.ProviderMessageSid is not null)
        {
            using var deadline = Deadline(ct);
            var original = notification.Content;
            var providerState = await _provider.RedactAsync(notification.ProviderMessageSid, deadline.Token);
            if (string.Equals(providerState.Body, original, StringComparison.Ordinal))
                providerState = await _provider.FetchAsync(notification.ProviderMessageSid, deadline.Token);
            if (string.Equals(providerState.Body, original, StringComparison.Ordinal))
                throw new NotificationOperationException("The provider did not dispose of the message content.");
            Apply(notification, providerState);
        }

        notification.DisposeContent();
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<ReconciliationView> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (from > to) throw new NotificationOperationException("'from' must be before or equal to 'to'.");
        using var deadline = Deadline(ct);
        var providerMessages = await _provider.ListAsync(from, to, deadline.Token);
        var local = await _db.OrderNotifications.AsNoTracking()
            .Where(x => x.ProviderMessageSid != null && x.CreatedAt >= from && x.CreatedAt <= to)
            .ToListAsync(ct);
        var localBySid = local.GroupBy(x => x.ProviderMessageSid!).ToDictionary(x => x.Key, x => x.First());
        var providerBySid = providerMessages.GroupBy(x => x.Sid).ToDictionary(x => x.Key, x => x.First());
        var entries = new List<ReconciliationEntry>();

        foreach (var provider in providerBySid.Values.OrderBy(x => x.CreatedAt))
        {
            localBySid.TryGetValue(provider.Sid, out var match);
            entries.Add(new ReconciliationEntry(provider.Sid, match?.Id,
                match is null ? "provider-only" : "matched", provider.Status,
                match?.DeliveryStatus, provider.ErrorCode, provider.CreatedAt, provider.SentAt));
        }
        foreach (var missing in local.Where(x => !providerBySid.ContainsKey(x.ProviderMessageSid!)))
            entries.Add(new ReconciliationEntry(missing.ProviderMessageSid!, missing.Id, "eshop-only",
                null, missing.DeliveryStatus, null, null, null));

        return new ReconciliationView(from, to, entries);
    }

    private async Task NotifyAllAsync(Order order, NotificationKind kind, string content,
        DateTimeOffset? scheduledFor, CancellationToken ct)
    {
        var contacts = await _db.ContactNumbers.AsNoTracking()
            .Where(x => x.ShopperId == order.BuyerId).OrderBy(x => x.Id).ToListAsync(CancellationToken.None);
        foreach (var contact in contacts)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, contact.Id, kind, content, scheduledFor);
            _db.OrderNotifications.Add(notification);
            await _db.SaveChangesAsync(CancellationToken.None);
            await SendPersistedAsync(notification, contact.CanonicalNumber, ct);
        }
    }

    private async Task SendPersistedAsync(OrderNotification notification, string destination, CancellationToken ct)
    {
        try
        {
            var providerState = await _provider.SendAsync(destination, notification.Content!, notification.ScheduledFor, ct);
            Apply(notification, providerState);
        }
        catch (Exception)
        {
            notification.RecordProviderFailure("provider-request-failed");
            _logger.LogWarning("Provider send failed for NotificationId {NotificationId} on OrderId {OrderId}.", notification.Id, notification.OrderId);
        }
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<List<NotificationView>> LoadAndRefreshAsync(string shopperId, int orderId, CancellationToken ct)
    {
        var notifications = await _db.OrderNotifications.Where(x => x.OrderId == orderId && x.ShopperId == shopperId)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync(CancellationToken.None);
        foreach (var notification in notifications) await RefreshAsync(notification, ct);
        await _db.SaveChangesAsync(CancellationToken.None);
        return notifications.Select(ToView).ToList();
    }

    private async Task RefreshAsync(OrderNotification notification, CancellationToken ct)
    {
        if (notification.ProviderMessageSid is null) return;
        try { Apply(notification, await _provider.FetchAsync(notification.ProviderMessageSid, ct)); }
        catch (Exception)
        {
            _logger.LogWarning("Provider status refresh failed for NotificationId {NotificationId} on OrderId {OrderId}.", notification.Id, notification.OrderId);
        }
    }

    private static Dictionary<int, int> NormalizeLines(IReadOnlyList<OrderLineRequest> lines)
    {
        if (lines is null || lines.Count == 0)
            throw new NotificationOperationException("At least one catalog item is required.");
        if (lines.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
            throw new NotificationOperationException("Catalog item identifiers and quantities must be positive.");
        return lines.GroupBy(x => x.CatalogItemId).ToDictionary(x => x.Key, x => checked(x.Sum(y => y.Quantity)));
    }

    private static void Apply(OrderNotification notification, ProviderMessage state) =>
        notification.RecordProviderState(state.Sid, state.Status, state.ErrorCode);

    private static NotificationView ToView(OrderNotification n) => new(n.Id, n.Kind.ToString(),
        n.DeliveryStatus, n.ProviderMessageSid, n.ProviderErrorCode, n.Content,
        n.CreatedAt, n.ScheduledFor, n.ContentDisposedAt);

    private static bool IsTerminal(string status) => status is "delivered" or "failed" or "undelivered" or "canceled" or "read";
    private static bool CanResend(string status) => status is "failed" or "undelivered" or "canceled" or "provider-request-failed";
    private static CancellationTokenSource Deadline(CancellationToken ct)
    {
        var result = CancellationTokenSource.CreateLinkedTokenSource(ct);
        result.CancelAfter(ProviderBudget);
        return result;
    }
}
