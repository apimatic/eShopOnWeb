using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    private static readonly Address PlaceholderAddress = new("N/A", "N/A", "N/A", "N/A", "N/A");

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderDelivery> _deliveries;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ISmsSender _smsSender;
    private readonly IUriComposer _uriComposer;
    private readonly NotificationSchedulingSettings _scheduling;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderDelivery> deliveries,
        IRepository<OrderNotification> notifications,
        ISmsSender smsSender,
        IUriComposer uriComposer,
        NotificationSchedulingSettings scheduling,
        IAppLogger<OrderNotificationService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _deliveries = deliveries;
        _notifications = notifications;
        _smsSender = smsSender;
        _uriComposer = uriComposer;
        _scheduling = scheduling;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, Address? shipToAddress,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
            throw new InvalidOrderRequestException("An order must contain at least one line.");
        if (lines.Any(l => l.Quantity <= 0))
            throw new InvalidOrderRequestException("Every order line must have a quantity of at least one.");

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var items = lines.Select(line =>
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new InvalidOrderRequestException($"Catalog item {line.CatalogItemId} does not exist.");
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress ?? PlaceholderAddress, items);
        await _orders.AddAsync(order, cancellationToken);

        var delivery = new OrderDelivery(order.Id, buyerId);
        await _deliveries.AddAsync(delivery, cancellationToken);

        _logger.LogInformation("Placed order {OrderId} for buyer {BuyerId}.", order.Id, buyerId);

        // Best-effort: telling the shopper must never fail the placement.
        await NotifyAsync(order.Id, buyerId, NotificationKind.OrderPlaced, cancellationToken);

        return order;
    }

    public async Task<bool> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var delivery = await _deliveries.FirstOrDefaultAsync(new OrderDeliveryByOrderSpecification(orderId), cancellationToken);
        if (delivery is null)
        {
            return false;
        }

        delivery.MarkDispatched();
        await _deliveries.UpdateAsync(delivery, cancellationToken);
        _logger.LogInformation("Dispatched order {OrderId}.", orderId);

        // Best-effort: tell the shopper it is on its way, and queue the delivery follow-up with the
        // provider for a few days later.
        await NotifyDispatchAsync(orderId, delivery.OwnerId, cancellationToken);

        return true;
    }

    public async Task<bool> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var delivery = await _deliveries.FirstOrDefaultAsync(new OrderDeliveryByOrderSpecification(orderId), cancellationToken);
        if (delivery is null)
        {
            return false;
        }

        delivery.MarkCancelled();
        await _deliveries.UpdateAsync(delivery, cancellationToken);
        _logger.LogInformation("Cancelled order {OrderId}.", orderId);

        // Critical: any follow-up not yet sent must never reach the shopper — call it off at the
        // provider before it goes out.
        await CancelPendingFollowUpsAsync(orderId, cancellationToken);

        // Best-effort: tell the shopper it was cancelled.
        await NotifyAsync(orderId, delivery.OwnerId, NotificationKind.OrderCancelled, cancellationToken);

        return true;
    }

    public async Task<IReadOnlyList<OrderDeliveryView>> GetMyOrdersAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));

        var deliveries = await _deliveries.ListAsync(new OrderDeliveriesByOwnerSpecification(ownerId), cancellationToken);
        if (deliveries.Count == 0)
        {
            return Array.Empty<OrderDeliveryView>();
        }

        var orderIds = deliveries.Select(d => d.OrderId).ToArray();
        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderIdsSpecification(orderIds), cancellationToken);

        // Bring each notification's outcome up to date from the provider before reporting it.
        await RefreshDeliveryStatusesAsync(notifications, cancellationToken);

        var views = new List<OrderDeliveryView>(deliveries.Count);
        foreach (var delivery in deliveries)
        {
            // Load the order with its items so the reported total is accurate.
            var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(delivery.OrderId), cancellationToken);
            var forOrder = notifications.Where(n => n.OrderId == delivery.OrderId).ToList();
            views.Add(new OrderDeliveryView(delivery, order, forOrder));
        }

        return views;
    }

    public async Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsAsync(int orderId, string ownerId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));

        var delivery = await _deliveries.FirstOrDefaultAsync(new OrderDeliveryByOrderSpecification(orderId), cancellationToken);
        if (delivery is null || delivery.OwnerId != ownerId)
        {
            // Not the caller's order (or not placed through the API): indistinguishable from "not found".
            return null;
        }

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshDeliveryStatusesAsync(notifications, cancellationToken);
        return notifications;
    }

    private async Task NotifyAsync(int orderId, string ownerId, NotificationKind kind, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
        if (numbers.Count == 0)
        {
            // A shopper with no number on file is simply not messaged.
            return;
        }

        var body = NotificationMessageBuilder.Build(kind, orderId);
        foreach (var number in numbers)
        {
            var notification = new OrderNotification(orderId, ownerId, kind, number.PhoneNumber);
            try
            {
                var result = await _smsSender.SendAsync(number.PhoneNumber, body, cancellationToken);
                notification.RecordAccepted(result.MessageSid, result.Status);
            }
            catch (SmsProviderException ex)
            {
                notification.RecordSendFailure();
                _logger.LogWarning("Could not send {Kind} notification for order {OrderId} (provider status {Status}).",
                    kind, orderId, ex.StatusCode);
            }
            await _notifications.AddAsync(notification, cancellationToken);
        }
    }

    private async Task NotifyDispatchAsync(int orderId, string ownerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
        if (numbers.Count == 0)
        {
            return;
        }

        var dispatchedBody = NotificationMessageBuilder.Build(NotificationKind.OrderDispatched, orderId);
        var followUpBody = NotificationMessageBuilder.Build(NotificationKind.DeliveryFollowUp, orderId);
        var sendAt = DateTimeOffset.UtcNow.Add(_scheduling.FollowUpDelay);

        foreach (var number in numbers)
        {
            // "On its way" now.
            var dispatched = new OrderNotification(orderId, ownerId, NotificationKind.OrderDispatched, number.PhoneNumber);
            try
            {
                var result = await _smsSender.SendAsync(number.PhoneNumber, dispatchedBody, cancellationToken);
                dispatched.RecordAccepted(result.MessageSid, result.Status);
            }
            catch (SmsProviderException ex)
            {
                dispatched.RecordSendFailure();
                _logger.LogWarning("Could not send dispatch notification for order {OrderId} (provider status {Status}).",
                    orderId, ex.StatusCode);
            }
            await _notifications.AddAsync(dispatched, cancellationToken);

            // "How was delivery?" queued with the provider for later.
            var followUp = new OrderNotification(orderId, ownerId, NotificationKind.DeliveryFollowUp,
                number.PhoneNumber, isScheduledFollowUp: true);
            try
            {
                var result = await _smsSender.ScheduleAsync(number.PhoneNumber, followUpBody, sendAt, cancellationToken);
                followUp.RecordAccepted(result.MessageSid, result.Status);
            }
            catch (SmsProviderException ex)
            {
                followUp.RecordSendFailure();
                _logger.LogWarning("Could not schedule delivery follow-up for order {OrderId} (provider status {Status}).",
                    orderId, ex.StatusCode);
            }
            await _notifications.AddAsync(followUp, cancellationToken);
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var pending = await _notifications.ListAsync(new PendingFollowUpByOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in pending)
        {
            try
            {
                await _smsSender.CancelScheduledAsync(followUp.MessageSid!, cancellationToken);
                followUp.MarkCanceledAtProvider();
                await _notifications.UpdateAsync(followUp, cancellationToken);
            }
            catch (SmsProviderException ex)
            {
                _logger.LogWarning("Could not cancel scheduled follow-up for order {OrderId} (provider status {Status}).",
                    orderId, ex.StatusCode);
            }
        }
    }

    private async Task RefreshDeliveryStatusesAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (notification.MessageSid is null || MessageDeliveryStatuses.IsTerminal(notification.Status))
            {
                continue;
            }

            try
            {
                var info = await _smsSender.FetchAsync(notification.MessageSid, cancellationToken);
                notification.UpdateDelivery(info.Status, info.ErrorCode, info.ErrorMessage);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (SmsProviderException ex)
            {
                // Keep the last known status; a transient read failure must not break the report.
                _logger.LogWarning("Could not refresh delivery status for notification {NotificationId} (provider status {Status}).",
                    notification.Id, ex.StatusCode);
            }
        }
    }
}
