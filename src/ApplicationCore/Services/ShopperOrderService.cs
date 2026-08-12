using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Places orders (reusing the existing order/order-item model) and moves them through their
/// lifecycle, keeping the shopper informed by SMS. Sending a message is always best-effort: a
/// message that cannot be sent is recorded as failed but never fails the order operation itself.
/// Shopper contact numbers are never written to logs.
/// </summary>
public class ShopperOrderService : IShopperOrderService
{
    // "a few days later" for the post-delivery follow-up. Well inside the provider's scheduling window.
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    // A placeholder ship-to address: this API places orders from catalog items only and does not
    // collect a shipping address, but the existing Order model requires one.
    private static Address PlaceholderAddress() => new("N/A", "N/A", "N/A", "N/A", "00000");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<Notification> _notificationRepository;
    private readonly ISmsProvider _smsProvider;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<ShopperOrderService> _logger;

    public ShopperOrderService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<Notification> notificationRepository,
        ISmsProvider smsProvider,
        IUriComposer uriComposer,
        IAppLogger<ShopperOrderService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _smsProvider = smsProvider;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineInput> lines,
        CancellationToken cancellationToken = default)
    {
        if (lines is null || lines.Count == 0 || lines.Any(l => l.Quantity <= 0))
        {
            return PlaceOrderResult.Failure(PlaceOrderError.NoItems);
        }

        var itemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(itemIds), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        if (itemIds.Any(id => !catalogById.ContainsKey(id)))
        {
            return PlaceOrderResult.Failure(PlaceOrderError.ItemNotFound);
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogById[line.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, PlaceholderAddress(), orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);

        await NotifyAsync(order, NotificationKind.OrderPlaced, BuildPlacedMessage(order), schedule: false, cancellationToken);

        return PlaceOrderResult.Success(order.Id);
    }

    public async Task<OrderOperationOutcome> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return OrderOperationOutcome.NotFound;
        }

        try
        {
            order.MarkDispatched();
        }
        catch (InvalidOrderStateException)
        {
            return OrderOperationOutcome.InvalidState;
        }

        await _orderRepository.UpdateAsync(order, cancellationToken);

        // Tell the shopper it is on its way, then queue a follow-up with the provider for later.
        await NotifyAsync(order, NotificationKind.OrderDispatched, BuildDispatchedMessage(order), schedule: false, cancellationToken);
        await NotifyAsync(order, NotificationKind.DeliveryFollowUp, BuildFollowUpMessage(order), schedule: true, cancellationToken);

        return OrderOperationOutcome.Success;
    }

    public async Task<OrderOperationOutcome> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return OrderOperationOutcome.NotFound;
        }

        try
        {
            order.MarkCancelled();
        }
        catch (InvalidOrderStateException)
        {
            return OrderOperationOutcome.InvalidState;
        }

        await _orderRepository.UpdateAsync(order, cancellationToken);

        // Call off any delivery follow-up that has not yet gone out: asking how a delivery went for a
        // cancelled order is exactly the incident this prevents.
        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in notifications.Where(n => n.IsCancellableFollowUp))
        {
            try
            {
                await _smsProvider.CancelScheduledAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.MarkCanceled();
                await _notificationRepository.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to call off scheduled follow-up (notification {0}) for order {1}: {2}",
                    followUp.Id, orderId, ex.GetType().Name);
            }
        }

        await NotifyAsync(order, NotificationKind.OrderCancelled, BuildCancelledMessage(order), schedule: false, cancellationToken);

        return OrderOperationOutcome.Success;
    }

    public async Task<IReadOnlyList<OrderSummary>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var notifications = await _notificationRepository.ListAsync(new NotificationsByOwnerSpecification(buyerId), cancellationToken);

        await RefreshStatusesAsync(notifications, cancellationToken);

        var byOrder = notifications.ToLookup(n => n.OrderId);

        return orders
            .OrderByDescending(o => o.Id)
            .Select(o => new OrderSummary(
                o.Id,
                o.Status,
                o.OrderDate,
                o.Total(),
                byOrder[o.Id].Select(ToView).ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<OrderNotificationView>?> GetOrderNotificationsAsync(int orderId, string buyerId,
        CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);

        // Not found and not-owned are deliberately indistinguishable so one shopper cannot probe for
        // another's orders.
        if (order is null || order.BuyerId != buyerId)
        {
            return null;
        }

        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshStatusesAsync(notifications, cancellationToken);

        return notifications.Select(ToView).ToList();
    }

    /// <summary>
    /// Sends (or schedules) one message per number the shopper has on file, recording a notification
    /// for each. A shopper with no number on file is simply not messaged. Any send failure is
    /// captured on the notification and never propagates to the order operation.
    /// </summary>
    private async Task NotifyAsync(Order order, NotificationKind kind, string body, bool schedule, CancellationToken cancellationToken)
    {
        var contactNumbers = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(order.BuyerId), cancellationToken);

        foreach (var contactNumber in contactNumbers)
        {
            var notification = new Notification(order.Id, order.BuyerId, contactNumber.E164Number, body, kind);

            try
            {
                if (schedule)
                {
                    var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
                    var scheduled = await _smsProvider.ScheduleAsync(contactNumber.E164Number, body, sendAt, cancellationToken);
                    notification.MarkScheduled(scheduled.ProviderMessageSid, sendAt, scheduled.Status);
                }
                else
                {
                    var sent = await _smsProvider.SendAsync(contactNumber.E164Number, body, cancellationToken);
                    notification.MarkSent(sent.ProviderMessageSid, sent.Status, sent.ErrorCode);
                }
            }
            catch (Exception ex)
            {
                // Never let a messaging failure fail the order operation. Record it and move on.
                _logger.LogWarning("Failed to send {0} notification for order {1}: {2}",
                    kind, order.Id, ex.GetType().Name);
                notification.MarkSendFailed(null);
            }

            await _notificationRepository.AddAsync(notification, cancellationToken);
        }
    }

    /// <summary>
    /// Brings each non-terminal notification that has a provider identifier in step with the
    /// provider's current delivery outcome. Failures to refresh are swallowed — a stale status is
    /// better than a failed read.
    /// </summary>
    private async Task RefreshStatusesAsync(IReadOnlyList<Notification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (notification.ProviderMessageSid is null || notification.IsTerminal)
            {
                continue;
            }

            try
            {
                var state = await _smsProvider.FetchStatusAsync(notification.ProviderMessageSid, cancellationToken);
                notification.UpdateDeliveryState(state.Status, state.ErrorCode);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to refresh delivery status for notification {0}: {1}",
                    notification.Id, ex.GetType().Name);
            }
        }
    }

    private static OrderNotificationView ToView(Notification n) => new(
        n.Id,
        n.OrderId,
        n.Kind,
        n.Status,
        n.ProviderMessageSid,
        n.ProviderErrorCode,
        n.CreatedAt,
        n.ScheduledSendAt,
        n.ContentDisposed);

    private static string BuildPlacedMessage(Order order) =>
        $"eShopOnWeb: Thanks! Your order #{order.Id} has been placed. Total: {order.Total():C}.";

    private static string BuildDispatchedMessage(Order order) =>
        $"eShopOnWeb: Good news — your order #{order.Id} is on its way!";

    private static string BuildFollowUpMessage(Order order) =>
        $"eShopOnWeb: How did the delivery of your order #{order.Id} go? We'd love your feedback.";

    private static string BuildCancelledMessage(Order order) =>
        $"eShopOnWeb: Your order #{order.Id} has been cancelled. If this is unexpected, please contact us.";
}
