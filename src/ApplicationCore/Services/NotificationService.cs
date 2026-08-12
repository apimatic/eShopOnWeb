using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Flows 2 &amp; 3 — sends the messages that go out as an order moves and services the operator
/// actions on them. Order-lifecycle transitions always commit for the underlying order; a message
/// that cannot be sent is recorded against the order and never thrown, so the caller's request still
/// succeeds. A shopper with no number on file is simply not messaged.
/// </summary>
public class NotificationService : INotificationService
{
    /// <summary>How far ahead the delivery follow-up is queued with the provider (within the provider's 15-minute–35-day window).</summary>
    private const int FollowUpDelayDays = 3;

    // API orders carry no shipping address; the existing Order model requires one, so a clearly-marked
    // placeholder is used. This is not a secret and is safe to hard-code.
    private static Address PlaceholderAddress() => new("N/A (placed via API)", "N/A", "N/A", "N/A", "00000");

    private readonly IRepository<Order> _orders;
    private readonly IRepository<Notification> _notifications;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ISmsProvider _smsProvider;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<NotificationService> _logger;

    public NotificationService(
        IRepository<Order> orders,
        IRepository<Notification> notifications,
        IRepository<CatalogItem> catalogItems,
        IRepository<ContactNumber> contactNumbers,
        ISmsProvider smsProvider,
        IUriComposer uriComposer,
        IAppLogger<NotificationService> logger)
    {
        _orders = orders;
        _notifications = notifications;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _smsProvider = smsProvider;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    // ------------------------------------------------------------------ Flow 2: place / dispatch / cancel

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new ArgumentException("At least one order line is required.", nameof(lines));
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new ArgumentException("Each order line quantity must be greater than zero.", nameof(lines));
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var missing = ids.Where(id => !byId.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
        {
            throw new ArgumentException($"Unknown catalog item id(s): {string.Join(", ", missing)}.", nameof(lines));
        }

        var items = lines.Select(line =>
        {
            var catalogItem = byId[line.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, PlaceholderAddress(), items);
        order = await _orders.AddAsync(order, cancellationToken);

        await NotifyForOrderAsync(order, NotificationKind.OrderPlaced,
            $"eShop: your order #{order.Id} has been placed. Thank you for shopping with us!", cancellationToken);

        return order;
    }

    public async Task<Order?> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            return null;
        }

        // The transition itself is a real operation and may legitimately fail (e.g. already dispatched);
        // that is surfaced to the operator. Messaging afterwards is best-effort.
        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);

        await NotifyForOrderAsync(order, NotificationKind.OrderDispatched,
            $"eShop: good news — your order #{order.Id} is on its way!", cancellationToken);

        await ScheduleDeliveryFollowUpAsync(order, cancellationToken);

        return order;
    }

    public async Task<Order?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            return null;
        }

        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);

        // Call off any not-yet-sent delivery follow-up FIRST, so a "how did delivery go?" message can
        // never reach a customer whose order was cancelled.
        await CancelScheduledFollowUpsAsync(order.Id, cancellationToken);

        await NotifyForOrderAsync(order, NotificationKind.OrderCancelled,
            $"eShop: your order #{order.Id} has been cancelled. If this is unexpected, please contact support.", cancellationToken);

        return order;
    }

    public async Task<IReadOnlyList<OrderWithNotifications>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var result = new List<OrderWithNotifications>();
        foreach (var order in orders)
        {
            var notifications = await _notifications.ListAsync(new NotificationsByOrderSpecification(order.Id), cancellationToken);
            await SyncStatusesAsync(notifications, cancellationToken);
            result.Add(new OrderWithNotifications(order, notifications));
        }
        return result;
    }

    public async Task<IReadOnlyList<Notification>?> GetOrderNotificationsForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            // Not found, or not the caller's order — indistinguishable to the caller by design.
            return null;
        }

        var notifications = await _notifications.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        await SyncStatusesAsync(notifications, cancellationToken);
        return notifications;
    }

    // ------------------------------------------------------------------ Flow 3: resend / redact / reconcile

    public async Task<Notification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        // Idempotency: a repeat under the same key returns the message the first request produced and
        // sends nothing more. A genuine second attempt uses a fresh key.
        var priorForKey = await _notifications.FirstOrDefaultAsync(new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (priorForKey is not null)
        {
            _logger.LogInformation("Resend under an existing idempotency key returned notification {NotificationId}; nothing re-sent.", priorForKey.Id);
            return priorForKey;
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            return null;
        }

        // Re-send the same text where we still have it; if its content was disposed of, fall back to a generic line.
        var body = original.Body ?? $"eShop: an update about your order #{original.OrderId}.";

        var resend = new Notification(
            original.BuyerId, original.OrderId, NotificationKind.Resend, original.ToNumber, body,
            idempotencyKey: idempotencyKey, parentNotificationId: original.Id);

        try
        {
            var pm = await _smsProvider.SendAsync(original.ToNumber, body, cancellationToken);
            resend.ApplyProviderResult(pm.Sid, pm.Status ?? "queued", pm.ErrorCode, pm.ErrorMessage);
        }
        catch (Exception ex)
        {
            // The resend request still succeeds and the attempt is recorded; the key is stored either way,
            // so a replay never produces a second message.
            resend.MarkSendFailed(ex.Message);
            _logger.LogWarning("Resend of notification {NotificationId} could not be sent: {Error}", original.Id, ex.Message);
        }

        await _notifications.AddAsync(resend, cancellationToken);
        return resend;
    }

    public async Task<Notification?> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return null;
        }

        // Dispose of the text at the provider so it is no longer retrievable there. If this fails we do
        // not claim success — the exception surfaces — because "hidden by this application" is not enough.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            await _smsProvider.RedactBodyAsync(notification.ProviderMessageSid!, cancellationToken);
        }

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Disposed of the content of notification {NotificationId}; the record and its outcome survive.", notificationId);
        return notification;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider for its record of messages from our configured sending number over the range,
        // then line them up against what eShop believes it sent.
        var providerMessages = await _smsProvider.ListMessagesFromConfiguredNumberAsync(from, to, cancellationToken);
        var ourNotifications = await _notifications.ListAsync(new NotificationsWithProviderSidBetweenSpecification(from, to), cancellationToken);

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First());
        var ourBySid = ourNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationMatch>();
        foreach (var pair in ourBySid)
        {
            if (providerBySid.TryGetValue(pair.Key, out var providerMsg))
            {
                matched.Add(new ReconciliationMatch(pair.Key, providerMsg.Status, pair.Value.Id, pair.Value.Status));
            }
        }

        // Provider knows about it, eShop does not (e.g. the account's other, non-application traffic).
        var onlyAtProvider = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid) && !ourBySid.ContainsKey(m.Sid))
            .ToList();

        // eShop believes it sent it, the provider's range query did not return it.
        var onlyInEShop = ourNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid) && !providerBySid.ContainsKey(n.ProviderMessageSid!))
            .ToList();

        return new ReconciliationReport(from, to, _smsProvider.FromNumber, matched, onlyAtProvider, onlyInEShop);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Sends one message per registered number for the order's buyer. Never throws.</summary>
    private async Task NotifyForOrderAsync(Order order, NotificationKind kind, string body, CancellationToken cancellationToken)
    {
        try
        {
            var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
            if (numbers.Count == 0)
            {
                _logger.LogInformation("Order {OrderId}: no contact number on file; {Kind} notification skipped.", order.Id, kind);
                return;
            }

            foreach (var number in numbers)
            {
                var notification = new Notification(order.BuyerId, order.Id, kind, number.PhoneNumber, body);
                try
                {
                    var pm = await _smsProvider.SendAsync(number.PhoneNumber, body, cancellationToken);
                    notification.ApplyProviderResult(pm.Sid, pm.Status ?? "queued", pm.ErrorCode, pm.ErrorMessage);
                }
                catch (Exception ex)
                {
                    notification.MarkSendFailed(ex.Message);
                    _logger.LogWarning("Order {OrderId}: {Kind} notification could not be sent: {Error}", order.Id, kind, ex.Message);
                }
                await _notifications.AddAsync(notification, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            // A messaging failure must never fail the underlying order operation.
            _logger.LogWarning("Order {OrderId}: {Kind} notification step failed: {Error}", order.Id, kind, ex.Message);
        }
    }

    /// <summary>Queues the delivery follow-up with the provider for a few days later. Never throws.</summary>
    private async Task ScheduleDeliveryFollowUpAsync(Order order, CancellationToken cancellationToken)
    {
        try
        {
            var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
            if (numbers.Count == 0)
            {
                return;
            }

            var sendAt = DateTimeOffset.UtcNow.AddDays(FollowUpDelayDays);
            var body = $"eShop: how did the delivery of your order #{order.Id} go? We'd love your feedback.";

            foreach (var number in numbers)
            {
                var notification = new Notification(order.BuyerId, order.Id, NotificationKind.DeliveryFollowUp, number.PhoneNumber, body, scheduledSendAt: sendAt);
                try
                {
                    var pm = await _smsProvider.ScheduleAsync(number.PhoneNumber, body, sendAt, cancellationToken);
                    notification.ApplyProviderResult(pm.Sid, pm.Status ?? "scheduled", pm.ErrorCode, pm.ErrorMessage);
                }
                catch (Exception ex)
                {
                    notification.MarkSendFailed(ex.Message);
                    _logger.LogWarning("Order {OrderId}: delivery follow-up could not be scheduled: {Error}", order.Id, ex.Message);
                }
                await _notifications.AddAsync(notification, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Order {OrderId}: scheduling the delivery follow-up failed: {Error}", order.Id, ex.Message);
        }
    }

    /// <summary>Cancels any not-yet-sent delivery follow-ups for an order at the provider. Never throws.</summary>
    private async Task CancelScheduledFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var scheduled = await _notifications.ListAsync(new ScheduledFollowUpsByOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in scheduled)
        {
            try
            {
                await _smsProvider.CancelScheduledAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.MarkCanceled();
                await _notifications.UpdateAsync(followUp, cancellationToken);
                _logger.LogInformation("Order {OrderId}: cancelled scheduled delivery follow-up {NotificationId}.", orderId, followUp.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Order {OrderId}: failed to cancel scheduled follow-up {NotificationId}: {Error}", orderId, followUp.Id, ex.Message);
            }
        }
    }

    /// <summary>Refreshes the stored delivery outcome from the provider for any non-terminal message. Never throws.</summary>
    private async Task SyncStatusesAsync(IReadOnlyList<Notification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid) || notification.IsTerminalStatus())
            {
                continue;
            }

            try
            {
                var pm = await _smsProvider.FetchAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.UpdateDeliveryState(pm.Status ?? notification.Status ?? "unknown", pm.ErrorCode, pm.ErrorMessage);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not refresh status for notification {NotificationId}: {Error}", notification.Id, ex.Message);
            }
        }
    }
}
