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
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>"A few days later" for the post-delivery follow-up (within Twilio's 15min–35day window).</summary>
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    // API-placed orders carry no shipping address of their own; reuse the sample's default, as the
    // storefront checkout does, so the existing Order model is honoured unchanged.
    private static readonly Address DefaultShipToAddress = new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<ContactNumber> _contactRepository;
    private readonly IRepository<Notification> _notificationRepository;
    private readonly ISmsSender _smsSender;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<ContactNumber> contactRepository,
        IRepository<Notification> notificationRepository,
        ISmsSender smsSender,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _contactRepository = contactRepository;
        _notificationRepository = notificationRepository;
        _smsSender = smsSender;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    // --- Placing an order ------------------------------------------------------------------------

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> items, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(items, nameof(items));
        if (items.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one item.", nameof(items));
        }

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var orderItems = new List<OrderItem>();
        foreach (var line in items)
        {
            if (line.Quantity <= 0)
            {
                throw new ArgumentException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.", nameof(items));
            }
            if (!byId.TryGetValue(line.CatalogItemId, out var catalogItem))
            {
                throw new ArgumentException($"Catalog item {line.CatalogItemId} does not exist.", nameof(items));
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, DefaultShipToAddress, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);

        await NotifyOrderEventAsync(order, NotificationKind.OrderPlaced, cancellationToken);
        return order;
    }

    // --- Dispatch --------------------------------------------------------------------------------

    public async Task<Order?> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order == null) return null;

        order.Dispatch(); // throws InvalidOrderStateException on an illegal transition
        await _orderRepository.UpdateAsync(order, cancellationToken);

        // Tell the shopper it is on its way now...
        await NotifyOrderEventAsync(order, NotificationKind.OrderDispatched, cancellationToken);
        // ...and queue the "how did delivery go?" follow-up with the provider for a few days later.
        await ScheduleFollowUpAsync(order, cancellationToken);

        return order;
    }

    // --- Cancel ----------------------------------------------------------------------------------

    public async Task<Order?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order == null) return null;

        order.Cancel(); // throws InvalidOrderStateException on an illegal transition
        await _orderRepository.UpdateAsync(order, cancellationToken);

        // Call off any follow-up that has not yet gone out BEFORE anything else — a "how did
        // delivery go?" text for a cancelled order is exactly the incident we must prevent.
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        // Then tell the shopper the order was cancelled.
        await NotifyOrderEventAsync(order, NotificationKind.OrderCancelled, cancellationToken);

        return order;
    }

    // --- Reads -----------------------------------------------------------------------------------

    public async Task<IReadOnlyList<OrderWithNotifications>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var result = new List<OrderWithNotifications>();
        foreach (var order in orders)
        {
            var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(order.Id), cancellationToken);
            await RefreshStatusesAsync(notifications, cancellationToken);
            result.Add(new OrderWithNotifications(order, notifications));
        }
        return result;
    }

    public async Task<IReadOnlyList<Notification>?> GetOrderNotificationsAsync(int orderId, string? buyerScope, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order == null) return null;
        if (buyerScope != null && order.BuyerId != buyerScope) return null; // a shopper only sees their own order

        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshStatusesAsync(notifications, cancellationToken);
        return notifications;
    }

    // --- Resend ----------------------------------------------------------------------------------

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        // If this key was already used, return the earlier result rather than sending again.
        var prior = await _notificationRepository.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (prior != null)
        {
            return new ResendResult(ResendOutcome.Duplicate, prior);
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original == null)
        {
            return new ResendResult(ResendOutcome.NotFound, null);
        }

        // Refresh the original's outcome so we don't re-send something that actually reached them.
        await RefreshStatusesAsync(new[] { original }, cancellationToken);
        if (original.DeliveryStatus == NotificationDeliveryStatus.Delivered)
        {
            return new ResendResult(ResendOutcome.AlreadyDelivered, original);
        }
        if (original.ContentRedacted || string.IsNullOrEmpty(original.Body))
        {
            return new ResendResult(ResendOutcome.ContentDisposed, original);
        }

        var resend = Notification.ForImmediate(original.OrderId, original.BuyerId, original.Kind, original.ToPhoneNumber, original.Body);
        resend.SetResendMetadata(original.Id, idempotencyKey);
        await SendImmediateAsync(resend, cancellationToken);
        await _notificationRepository.AddAsync(resend, cancellationToken);

        return new ResendResult(ResendOutcome.Sent, resend);
    }

    // --- Content disposal (redaction) ------------------------------------------------------------

    public async Task<bool> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification == null) return false;

        // Dispose of the text at the provider so it is no longer retrievable there either. If this
        // fails we deliberately let it surface, rather than falsely reporting the content disposed.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            await _smsSender.RedactContentAsync(notification.ProviderMessageSid, cancellationToken);
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        return true;
    }

    // --- Reconciliation --------------------------------------------------------------------------

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // The provider is asked to filter by our sending number and date range; refine to the exact
        // instants here (the provider's date filter is day-granular).
        var providerMessages = await _smsSender.ListSentMessagesAsync(from, to, cancellationToken);
        var providerInRange = providerMessages
            .Where(m =>
            {
                var when = m.DateSent ?? m.DateCreated;
                return when.HasValue && when.Value >= from && when.Value <= to;
            })
            .GroupBy(m => m.Sid)
            .Select(g => g.First())
            .ToList();
        var providerBySid = providerInRange.ToDictionary(m => m.Sid);

        var eshopNotifications = await _notificationRepository.ListAsync(new NotificationsCreatedBetweenSpecification(from, to), cancellationToken);
        var eshopBySid = eshopNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationLine>();
        var onlyAtProvider = new List<ReconciliationLine>();
        var onlyInEShop = new List<ReconciliationLine>();

        foreach (var m in providerInRange)
        {
            if (eshopBySid.TryGetValue(m.Sid, out var n))
            {
                matched.Add(new ReconciliationLine(m.Sid, n.Id, n.OrderId, n.DeliveryStatus, m.Status, m.ErrorCode, m.DateSent ?? m.DateCreated));
            }
            else
            {
                onlyAtProvider.Add(new ReconciliationLine(m.Sid, null, null, null, m.Status, m.ErrorCode, m.DateSent ?? m.DateCreated));
            }
        }

        foreach (var n in eshopNotifications)
        {
            var hasProviderRecord = !string.IsNullOrEmpty(n.ProviderMessageSid) && providerBySid.ContainsKey(n.ProviderMessageSid!);
            if (!hasProviderRecord)
            {
                onlyInEShop.Add(new ReconciliationLine(n.ProviderMessageSid, n.Id, n.OrderId, n.DeliveryStatus, null, n.ErrorCode, null));
            }
        }

        return new ReconciliationReport(from, to, _smsSender.FromNumber, matched, onlyAtProvider, onlyInEShop);
    }

    // --- Helpers ---------------------------------------------------------------------------------

    /// <summary>Sends the event's message to each of the buyer's numbers; never throws.</summary>
    private async Task NotifyOrderEventAsync(Order order, NotificationKind kind, CancellationToken cancellationToken)
    {
        try
        {
            var numbers = await _contactRepository.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
            if (numbers.Count == 0) return; // no number on file → simply not messaged

            var body = NotificationMessages.For(kind, order.Id);
            foreach (var number in numbers)
            {
                var notification = Notification.ForImmediate(order.Id, order.BuyerId, kind, number.PhoneNumber, body);
                await SendImmediateAsync(notification, cancellationToken);
                await _notificationRepository.AddAsync(notification, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            // A messaging problem must never fail the order operation.
            _logger.LogWarning($"Notifying order {order.Id} ({kind}) did not fully complete: {ex.GetType().Name}.");
        }
    }

    /// <summary>Schedules the delivery follow-up with the provider for each of the buyer's numbers.</summary>
    private async Task ScheduleFollowUpAsync(Order order, CancellationToken cancellationToken)
    {
        try
        {
            var numbers = await _contactRepository.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
            if (numbers.Count == 0) return;

            var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
            var body = NotificationMessages.For(NotificationKind.DeliveryFollowUp, order.Id);
            foreach (var number in numbers)
            {
                var notification = Notification.ForScheduled(order.Id, order.BuyerId, NotificationKind.DeliveryFollowUp, number.PhoneNumber, body, sendAt);
                try
                {
                    var result = await _smsSender.ScheduleAsync(number.PhoneNumber, body, sendAt, cancellationToken);
                    notification.RecordProviderResult(result.Sid, result.Status, result.ErrorCode);
                }
                catch (Exception ex)
                {
                    notification.RecordSendFailure();
                    _logger.LogWarning($"Scheduling follow-up for order {order.Id} failed: {ex.GetType().Name}.");
                }
                await _notificationRepository.AddAsync(notification, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Scheduling follow-ups for order {order.Id} did not fully complete: {ex.GetType().Name}.");
        }
    }

    /// <summary>Calls off scheduled follow-ups for an order that have not yet been sent.</summary>
    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        try
        {
            var pending = await _notificationRepository.ListAsync(new PendingScheduledNotificationsByOrderSpecification(orderId), cancellationToken);
            foreach (var notification in pending)
            {
                try
                {
                    await _smsSender.CancelScheduledAsync(notification.ProviderMessageSid!, cancellationToken);
                    notification.MarkCanceled();
                    await _notificationRepository.UpdateAsync(notification, cancellationToken);
                }
                catch (Exception ex)
                {
                    // Leave the status as scheduled so it honestly reflects that we could not cancel.
                    _logger.LogWarning($"Cancelling scheduled follow-up {notification.Id} for order {orderId} failed: {ex.GetType().Name}.");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Cancelling follow-ups for order {orderId} did not fully complete: {ex.GetType().Name}.");
        }
    }

    /// <summary>Sends a message now and records the outcome onto the notification; never throws.</summary>
    private async Task SendImmediateAsync(Notification notification, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _smsSender.SendAsync(notification.ToPhoneNumber, notification.Body!, cancellationToken);
            notification.RecordProviderResult(result.Sid, result.Status, result.ErrorCode);
        }
        catch (Exception ex)
        {
            notification.RecordSendFailure();
            _logger.LogWarning($"Sending {notification.Kind} for order {notification.OrderId} failed: {ex.GetType().Name}.");
        }
    }

    /// <summary>Refreshes each notification's delivery outcome from the provider where it can still change.</summary>
    private async Task RefreshStatusesAsync(IReadOnlyList<Notification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid)) continue;
            if (NotificationDeliveryStatus.IsTerminal(notification.DeliveryStatus)) continue;

            try
            {
                var status = await _smsSender.GetStatusAsync(notification.ProviderMessageSid, cancellationToken);
                notification.UpdateDeliveryStatus(status.Status, status.ErrorCode);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Refreshing status of notification {notification.Id} failed: {ex.GetType().Name}.");
            }
        }
    }

    private static class NotificationMessages
    {
        public static string For(NotificationKind kind, int orderId) => kind switch
        {
            NotificationKind.OrderPlaced => $"eShop: your order #{orderId} has been placed. Thank you for shopping with us!",
            NotificationKind.OrderDispatched => $"eShop: good news - your order #{orderId} is on its way!",
            NotificationKind.DeliveryFollowUp => $"eShop: how did the delivery of your order #{orderId} go? We'd love your feedback.",
            NotificationKind.OrderCancelled => $"eShop: your order #{orderId} has been cancelled. If this is unexpected, please contact us.",
            _ => $"eShop: an update about your order #{orderId}."
        };
    }
}
