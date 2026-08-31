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
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    // Provider scheduling window guardrails (validated app-side; the provider rejects violations with a 400).
    private static readonly TimeSpan MinScheduleLead = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MaxScheduleHorizon = TimeSpan.FromDays(7);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly INotificationGateway _notificationGateway;
    private readonly IAppLogger<OrderNotificationService> _logger;
    private readonly IUriComposer _uriComposer;
    private readonly TimeSpan _followUpDelay;

    public OrderNotificationService(IRepository<Order> orderRepository,
        IRepository<OrderNotification> notificationRepository,
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<CatalogItem> itemRepository,
        INotificationGateway notificationGateway,
        IAppLogger<OrderNotificationService> logger,
        IUriComposer uriComposer,
        TimeSpan followUpDelay)
    {
        _orderRepository = orderRepository;
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _itemRepository = itemRepository;
        _notificationGateway = notificationGateway;
        _logger = logger;
        _uriComposer = uriComposer;
        _followUpDelay = followUpDelay;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items, Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items.Count == 0)
        {
            throw new DomainConflictException("An order must contain at least one item.");
        }

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(items.Select(i => i.CatalogItemId).ToArray()), cancellationToken);

        var orderItems = new List<OrderItem>();
        foreach (var item in items)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == item.CatalogItemId);
            if (catalogItem == null)
            {
                throw new DomainConflictException($"Catalog item {item.CatalogItemId} does not exist.");
            }
            if (item.Units <= 0)
            {
                throw new DomainConflictException("Item quantities must be positive.");
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, item.Units));
        }

        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        await NotifyBuyerAsync(order, NotificationKind.OrderPlaced, NotificationMessages.OrderPlaced(order),
            schedule: false, cancellationToken: cancellationToken);

        return order;
    }

    public async Task<Order?> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order == null)
        {
            return null;
        }
        if (order.Status != OrderStatus.Placed)
        {
            throw new DomainConflictException($"Order {order.Id} is not in a dispatchable state ({order.Status}).");
        }

        order.MarkDispatched();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        await NotifyBuyerAsync(order, NotificationKind.OrderDispatched, NotificationMessages.OrderDispatched(order),
            schedule: false, cancellationToken: cancellationToken);

        var sendAt = DateTimeOffset.UtcNow.Add(_followUpDelay);
        if (sendAt - DateTimeOffset.UtcNow < MinScheduleLead || sendAt - DateTimeOffset.UtcNow > MaxScheduleHorizon)
        {
            _logger.LogWarning("Follow-up for order {OrderId} not scheduled: computed send time is outside the provider's scheduling window.", order.Id);
        }
        else
        {
            await NotifyBuyerAsync(order, NotificationKind.DeliveryFollowUp, NotificationMessages.DeliveryFollowUp(order),
                schedule: true, sendAt: sendAt, cancellationToken: cancellationToken);
        }

        return order;
    }

    public async Task<Order?> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order == null)
        {
            return null;
        }
        if (order.Status == OrderStatus.Cancelled)
        {
            throw new DomainConflictException($"Order {order.Id} is already cancelled.");
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        await NotifyBuyerAsync(order, NotificationKind.OrderCancelled, NotificationMessages.OrderCancelled(order),
            schedule: false, cancellationToken: cancellationToken);

        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        return order;
    }

    public async Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListNotificationsAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshOutcomesAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var alreadyProcessed = await _notificationRepository.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (alreadyProcessed != null)
        {
            _logger.LogInformation("Resend under an already-used idempotency key; returning the original result (notification {NotificationId}).", alreadyProcessed.Id);
            return alreadyProcessed;
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original == null)
        {
            return null;
        }
        if (original.IsContentRedacted || original.Body == null)
        {
            throw new DomainConflictException($"Notification {original.Id} can no longer be re-sent: its content has been disposed of.");
        }

        var resend = new OrderNotification(original.OrderId, original.BuyerId, original.Kind,
            original.ToNumber, original.Body, idempotencyKey: idempotencyKey);
        await _notificationRepository.AddAsync(resend, cancellationToken);

        await TrySendAsync(resend, schedule: false, sendAt: null, cancellationToken);
        return resend;
    }

    public async Task<bool> DeleteContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification == null)
        {
            return false;
        }

        if (!notification.IsContentRedacted)
        {
            if (notification.MessageSid != null)
            {
                await _notificationGateway.RedactMessageBodyAsync(notification.MessageSid, cancellationToken);
            }
            notification.RedactContent();
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }

        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _notificationGateway.ListMessagesAsync(from, to, cancellationToken);
        var localNotifications = await _notificationRepository.ListAsync(new NotificationsInRangeSpecification(from, to), cancellationToken);

        var localBySid = localNotifications
            .Where(n => n.MessageSid != null)
            .GroupBy(n => n.MessageSid!)
            .ToDictionary(g => g.Key, g => g.First());
        var providerBySid = providerMessages.ToDictionary(m => m.Sid, m => m);

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var localOnly = new List<ReconciliationEntry>();

        foreach (var providerMessage in providerMessages)
        {
            if (localBySid.TryGetValue(providerMessage.Sid, out var local))
            {
                matched.Add(new ReconciliationEntry(providerMessage.Sid, local.Id, local.OrderId,
                    providerMessage.Status, local.Status,
                    string.Equals(providerMessage.Status, local.Status, StringComparison.OrdinalIgnoreCase)));
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry(providerMessage.Sid, null, null,
                    providerMessage.Status, null, false));
            }
        }

        foreach (var local in localNotifications)
        {
            if (local.MessageSid == null || !providerBySid.ContainsKey(local.MessageSid))
            {
                localOnly.Add(new ReconciliationEntry(local.MessageSid, local.Id, local.OrderId,
                    null, local.Status, false));
            }
        }

        return new ReconciliationReport(from, to, matched, providerOnly, localOnly);
    }

    private async Task NotifyBuyerAsync(Order order, NotificationKind kind, string body, bool schedule,
        DateTimeOffset? sendAt = null, CancellationToken cancellationToken = default)
    {
        var contactNumbers = await _contactNumberRepository.ListAsync(
            new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
        var destination = contactNumbers.FirstOrDefault();
        if (destination == null)
        {
            _logger.LogInformation("Order {OrderId}: buyer has no contact number on file; no {Kind} notification sent.", order.Id, kind);
            return;
        }

        var notification = new OrderNotification(order.Id, order.BuyerId, kind, destination.PhoneNumber, body, sendAt);
        await _notificationRepository.AddAsync(notification, cancellationToken);

        await TrySendAsync(notification, schedule, sendAt, cancellationToken);
    }

    private async Task TrySendAsync(OrderNotification notification, bool schedule, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        try
        {
            var sent = schedule
                ? await _notificationGateway.ScheduleMessageAsync(notification.ToNumber, notification.Body!, sendAt!.Value, cancellationToken)
                : await _notificationGateway.SendMessageAsync(notification.ToNumber, notification.Body!, cancellationToken);
            notification.MarkAccepted(sent.Sid, sent.Status, sent.DateSent);
        }
        catch (Exception ex) when (ex is NotificationProviderException or InvalidPhoneNumberException)
        {
            // A message that cannot be sent must never fail the underlying operation.
            _logger.LogWarning("Notification {NotificationId} for order {OrderId} could not be handed to the provider: {Reason}",
                notification.Id, notification.OrderId, ex.Message);
            notification.MarkFailed("failed", null, "The provider rejected or could not process the send request.");
        }
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in notifications.Where(n => n.Kind == NotificationKind.DeliveryFollowUp && n.IsCancellable()))
        {
            try
            {
                var cancelled = await _notificationGateway.CancelScheduledMessageAsync(followUp.MessageSid!, cancellationToken);
                followUp.UpdateOutcome(cancelled.Status, cancelled.ErrorCode, cancelled.ErrorMessage, cancelled.DateSent);
            }
            catch (NotificationProviderException ex)
            {
                // Too late to cancel (already sent) or provider failure: re-read the real outcome instead of assuming.
                _logger.LogWarning("Could not cancel scheduled follow-up {NotificationId}; refreshing its outcome. Reason: {Reason}", followUp.Id, ex.Message);
                await TryRefreshOutcomeAsync(followUp, cancellationToken);
            }
            await _notificationRepository.UpdateAsync(followUp, cancellationToken);
        }
    }

    private async Task RefreshOutcomesAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications.Where(n => n.MessageSid != null && !n.HasTerminalStatus()))
        {
            await TryRefreshOutcomeAsync(notification, cancellationToken);
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
    }

    private async Task TryRefreshOutcomeAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var current = await _notificationGateway.GetMessageAsync(notification.MessageSid!, cancellationToken);
            notification.UpdateOutcome(current.Status, current.ErrorCode, current.ErrorMessage, current.DateSent);
        }
        catch (NotificationProviderException ex)
        {
            _logger.LogWarning("Could not refresh outcome for notification {NotificationId}. Reason: {Reason}", notification.Id, ex.Message);
        }
    }
}
