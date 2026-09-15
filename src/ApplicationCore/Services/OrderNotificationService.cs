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

public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How far ahead the delivery-feedback follow-up is queued with the provider.</summary>
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ISmsGateway _smsGateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        ISmsGateway smsGateway,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _smsGateway = smsGateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines,
        Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines is null || lines.Count == 0)
            throw new Exceptions.InvalidOrderRequestException("An order must contain at least one line.");
        if (lines.Any(l => l.Quantity <= 0))
            throw new Exceptions.InvalidOrderRequestException("Every order line must have a quantity of at least 1.");

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var missing = ids.Where(id => !byId.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
            throw new Exceptions.InvalidOrderRequestException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = byId[line.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        // Reuse the app's existing Order aggregate rather than a parallel model.
        var order = new Order(buyerId, shipToAddress, items);
        await _orderRepository.AddAsync(order, cancellationToken);

        _logger.LogInformation("Placed order {OrderId}.", order.Id);

        // Best-effort: a messaging failure must never fail the order.
        await RaiseAsync(order, NotificationType.OrderPlaced, scheduledFor: null, cancellationToken);

        return order.Id;
    }

    public async Task<bool> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return false;
        }

        // State change must succeed regardless of messaging.
        order.Dispatch();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Dispatched order {OrderId}.", order.Id);

        await RaiseAsync(order, NotificationType.OrderDispatched, scheduledFor: null, cancellationToken);
        // Queue the delivery-feedback follow-up with the provider for a few days later.
        await RaiseAsync(order, NotificationType.DeliveryFeedbackRequest,
            scheduledFor: DateTimeOffset.UtcNow.Add(FollowUpDelay), cancellationToken);

        return true;
    }

    public async Task<bool> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return false;
        }

        order.Cancel();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Cancelled order {OrderId}.", order.Id);

        // Call off any not-yet-sent follow-up so a "how did delivery go?" message never reaches the
        // shopper for a cancelled order. Best-effort with a small retry, but never fails the cancel.
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        await RaiseAsync(order, NotificationType.OrderCancelled, scheduledFor: null, cancellationToken);

        return true;
    }

    public async Task<IReadOnlyList<OrderView>> GetOrdersForBuyerAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByBuyerSpecification(buyerId), cancellationToken);

        await RefreshFromProviderAsync(notifications, cancellationToken);

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => ToOrderView(o, byOrder.TryGetValue(o.Id, out var list) ? list : new List<OrderNotification>()))
            .ToList();
    }

    public async Task<IReadOnlyList<NotificationView>?> GetOrderNotificationsAsync(int orderId, string callerBuyerId,
        bool callerIsOperator, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        // A shopper may only see their own order's notifications; an operator may see any.
        if (!callerIsOperator && order.BuyerId != callerBuyerId)
        {
            return null;
        }

        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshFromProviderAsync(notifications, cancellationToken);

        return notifications.Select(NotificationMapping.ToView).ToList();
    }

    // ---- internals ----

    /// <summary>
    /// Raise a notification of the given type for every number the order's buyer has on file, sending
    /// (or scheduling) each message. If the buyer has no number on file, a single "not sent" record is
    /// kept so the state is visible. Never throws for a messaging failure.
    /// </summary>
    private async Task RaiseAsync(Order order, NotificationType type, DateTimeOffset? scheduledFor,
        CancellationToken cancellationToken)
    {
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
        var body = NotificationMessages.For(type, order);

        if (numbers.Count == 0)
        {
            var notMessaged = new OrderNotification(order.Id, order.BuyerId, type, body, toNumber: null);
            if (scheduledFor.HasValue)
            {
                notMessaged.MarkAsFollowUp(scheduledFor.Value);
            }
            notMessaged.MarkNotSent();
            await _notificationRepository.AddAsync(notMessaged, cancellationToken);
            return;
        }

        foreach (var number in numbers)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, type, body, number.PhoneNumber);
            if (scheduledFor.HasValue)
            {
                notification.MarkAsFollowUp(scheduledFor.Value);
            }
            await _notificationRepository.AddAsync(notification, cancellationToken);

            try
            {
                var state = await _smsGateway.SendAsync(
                    new SmsMessageRequest(number.PhoneNumber, body, scheduledFor), cancellationToken);
                notification.RecordAccepted(state.ProviderMessageId, state.Status, state.ProviderStatusRaw,
                    state.ErrorCode, state.ErrorMessage, state.SentAt);
            }
            catch (Exception ex)
            {
                notification.RecordSendError(ex.Message);
                _logger.LogWarning("Could not send {Type} notification {NotificationId} for order {OrderId}.",
                    type, notification.Id, order.Id);
            }

            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var pending = await _notificationRepository.ListAsync(new PendingFollowUpsByOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in pending)
        {
            if (string.IsNullOrEmpty(followUp.ProviderMessageId))
            {
                continue;
            }

            SmsMessageState? state = null;
            // A couple of attempts, because calling off the follow-up matters, but never fail the cancel.
            for (var attempt = 1; attempt <= 3 && state is null; attempt++)
            {
                try
                {
                    state = await _smsGateway.CancelScheduledAsync(followUp.ProviderMessageId, cancellationToken);
                }
                catch (Exception)
                {
                    _logger.LogWarning("Attempt {Attempt} to cancel follow-up {NotificationId} for order {OrderId} failed.",
                        attempt, followUp.Id, orderId);
                }
            }

            if (state is not null)
            {
                followUp.ApplyProviderState(state.Status, state.ProviderStatusRaw, state.ErrorCode, state.ErrorMessage, state.SentAt);
                await _notificationRepository.UpdateAsync(followUp, cancellationToken);
            }
        }
    }

    private async Task RefreshFromProviderAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (notification.ProviderMessageId is null || NotificationMapping.IsTerminal(notification.Status))
            {
                continue;
            }

            try
            {
                var state = await _smsGateway.GetMessageStateAsync(notification.ProviderMessageId, cancellationToken);
                notification.ApplyProviderState(state.Status, state.ProviderStatusRaw, state.ErrorCode, state.ErrorMessage, state.SentAt);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception)
            {
                _logger.LogWarning("Could not refresh status for notification {NotificationId}.", notification.Id);
            }
        }
    }

    private static OrderView ToOrderView(Order order, List<OrderNotification> notifications) =>
        new(
            order.Id,
            order.Status.ToString(),
            order.OrderDate,
            order.Total(),
            order.OrderItems.Select(i => new OrderLineView(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units)).ToList(),
            notifications.OrderBy(n => n.CreatedAt).Select(NotificationMapping.ToView).ToList());
}
