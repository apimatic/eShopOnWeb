using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    // The provider holds the follow-up and sends it later; well within Twilio's 7-day scheduling window.
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    // Provider statuses from which a message will not move — no point re-reading them.
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered", "undelivered", "failed", "canceled"
    };

    private readonly IRepository<Order> _orders;
    private readonly IReadRepository<CatalogItem> _catalogItems;
    private readonly IReadRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<Notification> _notifications;
    private readonly ISmsProvider _smsProvider;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orders,
        IReadRepository<CatalogItem> catalogItems,
        IReadRepository<ContactNumber> contactNumbers,
        IRepository<Notification> notifications,
        ISmsProvider smsProvider,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _smsProvider = smsProvider;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineItem> items, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(items, nameof(items));
        if (items.Count == 0)
        {
            throw new InvalidOrderRequestException("An order must contain at least one item.");
        }

        var orderItems = await BuildOrderItemsAsync(items, ct);

        // No storefront address is supplied through the API; use a placeholder so the existing,
        // address-required Order model is reused unchanged.
        var shipToAddress = new Address("N/A", "N/A", "N/A", "N/A", "00000");
        var order = new Order(buyerId, shipToAddress, orderItems);
        await _orders.AddAsync(order, ct);

        _logger.LogInformation("Order {OrderId} placed for buyer with {ItemCount} item(s).", order.Id, orderItems.Count);

        await SendOrderMessageAsync(buyerId, order.Id, NotificationType.OrderPlaced,
            $"eShop: your order #{order.Id} has been placed. Thank you!", ct);

        return order.Id;
    }

    public async Task DispatchOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(orderId, ct) ?? throw new OrderNotFoundException(orderId);

        // A domain-rule violation (already dispatched / cancelled) legitimately fails the operation.
        order.MarkDispatched();
        await _orders.UpdateAsync(order, ct);
        _logger.LogInformation("Order {OrderId} marked dispatched.", order.Id);

        await SendOrderMessageAsync(order.BuyerId, order.Id, NotificationType.OrderDispatched,
            $"eShop: your order #{order.Id} is on its way!", ct);

        // Queue the "how did delivery go?" follow-up with the provider for a few days later.
        await ScheduleFollowUpAsync(order.BuyerId, order.Id, ct);
    }

    public async Task CancelOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(orderId, ct) ?? throw new OrderNotFoundException(orderId);

        order.MarkCancelled();
        await _orders.UpdateAsync(order, ct);
        _logger.LogInformation("Order {OrderId} marked cancelled.", order.Id);

        // Call off any follow-up the provider is still holding, so it never reaches the shopper.
        await CancelPendingFollowUpsAsync(order.Id, ct);

        await SendOrderMessageAsync(order.BuyerId, order.Id, NotificationType.OrderCancelled,
            $"eShop: your order #{order.Id} has been cancelled.", ct);
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
    }

    public async Task<IReadOnlyList<Notification>> GetNotificationsForBuyerAsync(string buyerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var notifications = await _notifications.ListAsync(new NotificationsByBuyerSpecification(buyerId), ct);
        await RefreshDeliveryStateAsync(notifications, ct);
        return notifications;
    }

    public async Task<Order?> GetOrderByIdAsync(int orderId, CancellationToken ct = default)
    {
        return await _orders.GetByIdAsync(orderId, ct);
    }

    public async Task<IReadOnlyList<Notification>> GetNotificationsForOrderAsync(int orderId, CancellationToken ct = default)
    {
        var notifications = await _notifications.ListAsync(new NotificationsByOrderSpecification(orderId), ct);
        await RefreshDeliveryStateAsync(notifications, ct);
        return notifications;
    }

    private async Task<List<OrderItem>> BuildOrderItemsAsync(IReadOnlyCollection<OrderLineItem> items, CancellationToken ct)
    {
        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), ct);

        var orderItems = new List<OrderItem>();
        foreach (var line in items)
        {
            if (line.Quantity <= 0)
            {
                throw new InvalidOrderRequestException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }

            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new InvalidOrderRequestException($"Catalog item {line.CatalogItemId} does not exist.");

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        return orderItems;
    }

    /// <summary>
    /// Sends an immediate message to every number the shopper has on file, recording each attempt.
    /// A send failure is recorded on the notification and never propagated — the order operation
    /// still succeeds. A shopper with no number on file is simply not messaged.
    /// </summary>
    private async Task SendOrderMessageAsync(string buyerId, int orderId, NotificationType type, string body, CancellationToken ct)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
        foreach (var number in numbers)
        {
            var notification = new Notification(buyerId, orderId, type, number.E164Number, body);
            await _notifications.AddAsync(notification, ct);

            try
            {
                var result = await _smsProvider.SendAsync(number.E164Number, body, ct);
                notification.RecordSent(result.ProviderSid, result.Status, result.ErrorCode, result.ErrorMessage);
            }
            catch (SmsProviderException ex)
            {
                notification.RecordSendFailure(ex.Message);
                _logger.LogWarning("Order {OrderId} {Type} message could not be sent (notification {NotificationId}): {Reason}",
                    orderId, type, notification.Id, ex.Message);
            }

            await _notifications.UpdateAsync(notification, ct);
        }
    }

    /// <summary>Queues a follow-up with the provider to be sent later, per number on file.</summary>
    private async Task ScheduleFollowUpAsync(string buyerId, int orderId, CancellationToken ct)
    {
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var body = $"eShop: how did the delivery of your order #{orderId} go? We'd love your feedback.";

        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
        foreach (var number in numbers)
        {
            var notification = new Notification(buyerId, orderId, NotificationType.DeliveryFollowUp, number.E164Number, body);
            await _notifications.AddAsync(notification, ct);

            try
            {
                var result = await _smsProvider.ScheduleAsync(number.E164Number, body, sendAt, ct);
                notification.RecordScheduled(result.ProviderSid, result.Status, sendAt);
                _logger.LogInformation("Order {OrderId} follow-up scheduled (notification {NotificationId}, status {Status}).",
                    orderId, notification.Id, notification.DeliveryStatus);
            }
            catch (SmsProviderException ex)
            {
                notification.RecordSendFailure(ex.Message);
                _logger.LogWarning("Order {OrderId} follow-up could not be scheduled (notification {NotificationId}): {Reason}",
                    orderId, notification.Id, ex.Message);
            }

            await _notifications.UpdateAsync(notification, ct);
        }
    }

    /// <summary>
    /// Cancels every follow-up the provider is still holding for an order. Best-effort: a cancel that
    /// fails is recorded but does not fail the order cancellation.
    /// </summary>
    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken ct)
    {
        var followUps = await _notifications.ListAsync(new PendingFollowUpsByOrderSpecification(orderId), ct);
        foreach (var followUp in followUps)
        {
            if (followUp.ProviderMessageSid is null)
            {
                continue;
            }

            try
            {
                var state = await _smsProvider.CancelScheduledAsync(followUp.ProviderMessageSid, ct);
                followUp.UpdateDeliveryState(state.Status, state.ErrorCode, state.ErrorMessage);
                _logger.LogInformation("Order {OrderId} follow-up cancelled (notification {NotificationId}, status {Status}).",
                    orderId, followUp.Id, followUp.DeliveryStatus);
            }
            catch (SmsProviderException ex)
            {
                followUp.UpdateDeliveryState(NotificationDeliveryStatus.CancelFailed, ex.StatusCode, ex.Message);
                _logger.LogWarning("Order {OrderId} follow-up could not be cancelled (notification {NotificationId}): {Reason}",
                    orderId, followUp.Id, ex.Message);
            }

            await _notifications.UpdateAsync(followUp, ct);
        }
    }

    /// <summary>
    /// Refreshes each message's delivery outcome from the provider (a free read). Best-effort: a
    /// provider failure leaves the last-known status in place rather than failing the query.
    /// </summary>
    private async Task RefreshDeliveryStateAsync(IReadOnlyList<Notification> notifications, CancellationToken ct)
    {
        foreach (var notification in notifications)
        {
            if (notification.ProviderMessageSid is null || TerminalStatuses.Contains(notification.DeliveryStatus))
            {
                continue;
            }

            try
            {
                var state = await _smsProvider.GetMessageStateAsync(notification.ProviderMessageSid, ct);
                if (!string.Equals(state.Status, notification.DeliveryStatus, StringComparison.OrdinalIgnoreCase)
                    || state.ErrorCode != notification.ProviderErrorCode)
                {
                    notification.UpdateDeliveryState(state.Status, state.ErrorCode, state.ErrorMessage);
                    await _notifications.UpdateAsync(notification, ct);
                }
            }
            catch (SmsProviderException ex)
            {
                _logger.LogWarning("Could not refresh delivery state for notification {NotificationId}: {Reason}",
                    notification.Id, ex.Message);
            }
        }
    }
}
