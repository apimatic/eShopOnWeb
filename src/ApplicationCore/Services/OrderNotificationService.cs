using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NotFoundException = Microsoft.eShopWeb.ApplicationCore.Exceptions.NotFoundException;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How far out the "how did delivery go?" follow-up is queued with the provider.</summary>
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<Notification> _notifications;
    private readonly ISmsProvider _sms;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ContactNumber> contactNumbers,
        IRepository<Notification> notifications,
        ISmsProvider sms,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _sms = sms;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string ownerId, IReadOnlyList<OrderLineRequest> lines, Address shipToAddress, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines is null || lines.Count == 0)
        {
            throw new InvalidRequestException("An order must contain at least one item.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new InvalidRequestException("Every order line must have a quantity of at least one.");
        }

        var itemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(itemIds), cancellationToken);
        var missing = itemIds.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidRequestException($"Unknown catalog item(s): {string.Join(", ", missing)}.");
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(ownerId, shipToAddress, orderItems);
        order = await _orders.AddAsync(order, cancellationToken);
        _logger.LogInformation("Placed order {OrderId} for owner {OwnerId}.", order.Id, ownerId);

        await NotifyAsync(order, NotificationKind.OrderPlaced, NotificationMessages.OrderPlaced(order.Id), cancellationToken);
        return order;
    }

    public async Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken)
            ?? throw new NotFoundException($"Order {orderId} was not found.");

        order.Dispatch();
        await _orders.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Dispatched order {OrderId}.", order.Id);

        // Tell the shopper it is on its way, then queue the delivery follow-up with the provider for a few days later.
        await NotifyAsync(order, NotificationKind.OrderDispatched, NotificationMessages.OrderDispatched(order.Id), cancellationToken);
        await NotifyAsync(order, NotificationKind.DeliveryFollowUp, NotificationMessages.DeliveryFollowUp(order.Id), cancellationToken,
            scheduleAt: DateTimeOffset.UtcNow.Add(FollowUpDelay));
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken)
            ?? throw new NotFoundException($"Order {orderId} was not found.");

        order.Cancel();
        await _orders.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Cancelled order {OrderId}.", order.Id);

        // Call off any delivery follow-up that has not yet gone out: asking how a delivery went for a
        // cancelled order is exactly the incident this prevents.
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        await NotifyAsync(order, NotificationKind.OrderCancelled, NotificationMessages.OrderCancelled(order.Id), cancellationToken);
        return order;
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string ownerId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        return await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(ownerId), cancellationToken);
    }

    public async Task<Order?> GetOwnedOrderAsync(string ownerId, int orderId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || order.BuyerId != ownerId)
        {
            return null;
        }
        return order;
    }

    public async Task<IReadOnlyList<Notification>> GetNotificationsForOrdersAsync(IReadOnlyList<int> orderIds, CancellationToken cancellationToken)
    {
        var all = new List<Notification>();
        foreach (var orderId in orderIds.Distinct())
        {
            var forOrder = await _notifications.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
            foreach (var notification in forOrder)
            {
                await RefreshDeliveryOutcomeAsync(notification, cancellationToken);
            }
            all.AddRange(forOrder);
        }
        return all;
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notifications.ListAsync(new ScheduledFollowUpsByOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            if (followUp.ProviderMessageSid is null)
            {
                continue;
            }
            try
            {
                var result = await _sms.CancelScheduledAsync(followUp.ProviderMessageSid, cancellationToken);
                if (result.Accepted)
                {
                    followUp.MarkCancelled();
                    await _notifications.UpdateAsync(followUp, cancellationToken);
                    _logger.LogInformation("Called off scheduled follow-up {NotificationId} for order {OrderId}.", followUp.Id, orderId);
                }
                else
                {
                    _logger.LogWarning("Provider did not cancel follow-up {NotificationId} for order {OrderId}: {Status}.",
                        followUp.Id, orderId, result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error cancelling follow-up {NotificationId} for order {OrderId}: {Error}", followUp.Id, orderId, ex.Message);
            }
        }
    }

    private async Task NotifyAsync(Order order, NotificationKind kind, string body, CancellationToken cancellationToken, DateTimeOffset? scheduleAt = null)
    {
        // A shopper with no number on file is simply not messaged.
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(order.BuyerId), cancellationToken);
        foreach (var number in numbers)
        {
            var notification = Notification.Create(order.BuyerId, order.Id, kind, number.E164Number, body);
            try
            {
                if (scheduleAt.HasValue)
                {
                    var result = await _sms.ScheduleAsync(number.E164Number, body, scheduleAt.Value, cancellationToken);
                    if (result.Accepted && result.Sid is not null)
                    {
                        notification.RecordScheduled(result.Sid, result.Status ?? "scheduled", scheduleAt.Value);
                    }
                    else
                    {
                        notification.RecordSendFailure(result.ErrorCode, result.ErrorMessage);
                    }
                }
                else
                {
                    var result = await _sms.SendAsync(number.E164Number, body, cancellationToken);
                    if (result.Accepted && result.Sid is not null)
                    {
                        notification.RecordAccepted(result.Sid, result.Status ?? "queued");
                    }
                    else
                    {
                        notification.RecordSendFailure(result.ErrorCode, result.ErrorMessage);
                    }
                }
            }
            catch (Exception ex)
            {
                // A message that cannot be sent must never fail the underlying operation.
                _logger.LogWarning("Failed to send {Kind} message for order {OrderId}: {Error}", kind, order.Id, ex.Message);
                notification.RecordSendFailure(null, "The message could not be sent.");
            }

            await _notifications.AddAsync(notification, cancellationToken);
        }
    }

    private async Task RefreshDeliveryOutcomeAsync(Notification notification, CancellationToken cancellationToken)
    {
        if (!notification.IsDeliveryOutcomePending() || notification.ProviderMessageSid is null)
        {
            return;
        }
        try
        {
            var providerMessage = await _sms.FetchAsync(notification.ProviderMessageSid, cancellationToken);
            if (providerMessage is not null)
            {
                notification.UpdateDeliveryStatus(providerMessage.Status ?? string.Empty, providerMessage.ErrorCode, providerMessage.ErrorMessage);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to refresh delivery outcome for notification {NotificationId}: {Error}", notification.Id, ex.Message);
        }
    }
}
