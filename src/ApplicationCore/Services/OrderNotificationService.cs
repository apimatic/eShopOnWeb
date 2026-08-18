using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How far ahead the "how did it go?" follow-up is queued with the provider.</summary>
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<Notification> _notifications;
    private readonly ISmsGateway _smsGateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ContactNumber> contactNumbers,
        IRepository<Notification> notifications,
        ISmsGateway smsGateway,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _smsGateway = smsGateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, Address shipToAddress, CancellationToken ct = default)
    {
        if (lines is null || lines.Count == 0)
            return new PlaceOrderResult(false, null, "At least one order item is required.");

        if (lines.Any(l => l.Quantity <= 0))
            return new PlaceOrderResult(false, null, "Every order item must have a quantity of at least 1.");

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), ct);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var missing = ids.Where(id => !byId.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
            return new PlaceOrderResult(false, null, $"Unknown catalog item id(s): {string.Join(", ", missing)}.");

        var orderItems = lines.Select(line =>
        {
            var catalogItem = byId[line.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orders.AddAsync(order, ct);

        // The shopper is told their order was placed. A message that cannot be sent must never fail
        // the placement, so this is best-effort and swallows send failures into the notification record.
        await NotifyBuyerNumbersAsync(order.Id, buyerId, NotificationKind.OrderPlaced, NotificationMessages.OrderPlaced(order.Id), ct);

        return new PlaceOrderResult(true, order.Id, null);
    }

    public async Task<bool> DispatchAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(orderId, ct);
        if (order is null)
            return false;

        await NotifyBuyerNumbersAsync(order.Id, order.BuyerId, NotificationKind.OrderDispatched, NotificationMessages.OrderDispatched(order.Id), ct);

        // Queue the "how did it go?" follow-up WITH THE PROVIDER for a few days later — not held here.
        await ScheduleFollowUpsAsync(order.Id, order.BuyerId, ct);

        return true;
    }

    public async Task<bool> CancelAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(orderId, ct);
        if (order is null)
            return false;

        await NotifyBuyerNumbersAsync(order.Id, order.BuyerId, NotificationKind.OrderCancelled, NotificationMessages.OrderCancelled(order.Id), ct);

        // A follow-up that has not yet gone out must never reach the shopper: call it off at the provider.
        await CancelPendingFollowUpsAsync(order.Id, ct);

        return true;
    }

    public async Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var result = new List<OrderWithNotifications>();
        foreach (var order in orders.OrderByDescending(o => o.OrderDate))
        {
            var notifications = await LoadAndRefreshNotificationsAsync(order.Id, ct);
            result.Add(new OrderWithNotifications(order, notifications));
        }
        return result;
    }

    public async Task<(AccessOutcome Outcome, IReadOnlyList<Notification> Notifications)> GetOrderNotificationsAsync(
        int orderId, string requesterBuyerId, bool isAdmin, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(orderId, ct);
        if (order is null)
            return (AccessOutcome.NotFound, Array.Empty<Notification>());

        // A shopper acts only on their own order; an operator (admin) may view any.
        if (!isAdmin && !string.Equals(order.BuyerId, requesterBuyerId, StringComparison.Ordinal))
            return (AccessOutcome.Forbidden, Array.Empty<Notification>());

        var notifications = await LoadAndRefreshNotificationsAsync(orderId, ct);
        return (AccessOutcome.Ok, notifications);
    }

    // ----- helpers -------------------------------------------------------------------------

    private async Task NotifyBuyerNumbersAsync(int orderId, string buyerId, NotificationKind kind, string body, CancellationToken ct)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
        // A shopper with no number on file is simply not messaged.
        foreach (var number in numbers)
        {
            var notification = new Notification(orderId, buyerId, kind, number.PhoneNumber, body);
            try
            {
                var result = await _smsGateway.SendAsync(number.PhoneNumber, body, ct);
                notification.RecordSent(result.ProviderMessageId, result.Status, result.ErrorCode, result.ErrorMessage);
            }
            catch (SmsGatewayException ex)
            {
                // Never fail the underlying operation because a message could not be sent.
                _logger.LogWarning("Order {0} {1} SMS could not be sent (provider status {2}).", orderId, kind, ex.ProviderStatusCode);
                notification.MarkSendFailed(ex.Message);
            }
            await _notifications.AddAsync(notification, ct);
        }
    }

    private async Task ScheduleFollowUpsAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var body = NotificationMessages.DeliveryFollowUp(orderId);
        foreach (var number in numbers)
        {
            var notification = new Notification(orderId, buyerId, NotificationKind.DeliveryFollowUp, number.PhoneNumber, body);
            try
            {
                var result = await _smsGateway.ScheduleAsync(number.PhoneNumber, body, sendAt, ct);
                notification.MarkScheduled(result.ProviderMessageId, sendAt);
                if (!string.IsNullOrWhiteSpace(result.Status) && result.Status != NotificationDeliveryStatus.Scheduled)
                    notification.UpdateDeliveryState(result.Status, result.ErrorCode, result.ErrorMessage);
            }
            catch (SmsGatewayException ex)
            {
                _logger.LogWarning("Order {0} follow-up could not be scheduled (provider status {1}).", orderId, ex.ProviderStatusCode);
                notification.MarkSendFailed(ex.Message);
            }
            await _notifications.AddAsync(notification, ct);
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken ct)
    {
        var notifications = await _notifications.ListAsync(new NotificationsByOrderSpecification(orderId), ct);
        foreach (var notification in notifications.Where(n => n.IsPendingScheduledFollowUp))
        {
            try
            {
                await _smsGateway.CancelScheduledAsync(notification.ProviderMessageId!, ct);
                notification.MarkCanceled();
            }
            catch (SmsGatewayException ex)
            {
                // Could not cancel — re-read the provider's current view rather than assume.
                _logger.LogWarning("Order {0} follow-up cancel failed (provider status {1}); re-reading state.", orderId, ex.ProviderStatusCode);
                await TryRefreshAsync(notification, ct);
            }
            await _notifications.UpdateAsync(notification, ct);
        }
    }

    private async Task<IReadOnlyList<Notification>> LoadAndRefreshNotificationsAsync(int orderId, CancellationToken ct)
    {
        var notifications = await _notifications.ListAsync(new NotificationsByOrderSpecification(orderId), ct);
        foreach (var notification in notifications)
            await TryRefreshAsync(notification, ct);
        return notifications.ToList();
    }

    /// <summary>Best-effort refresh of a non-terminal message's delivery outcome from the provider.</summary>
    private async Task TryRefreshAsync(Notification notification, CancellationToken ct)
    {
        if (notification.ProviderMessageId is null || NotificationDeliveryStatus.IsTerminal(notification.DeliveryStatus))
            return;

        try
        {
            var state = await _smsGateway.GetDeliveryStateAsync(notification.ProviderMessageId, ct);
            if (state.Status != notification.DeliveryStatus || state.ErrorCode != notification.ErrorCode)
            {
                notification.UpdateDeliveryState(state.Status, state.ErrorCode, state.ErrorMessage);
                await _notifications.UpdateAsync(notification, ct);
            }
        }
        catch (SmsGatewayException ex)
        {
            // Reading the outcome must never fail the read of an order or its notifications.
            _logger.LogWarning("Could not refresh notification {0} state (provider status {1}).", notification.Id, ex.ProviderStatusCode);
        }
    }
}
