using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Places orders (reusing the existing order/order-item model) and drives the SMS notifications
/// that go out as an order moves. Every provider interaction happens through <see cref="ISmsGateway"/>.
/// A failed message never fails the underlying order operation.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    // The follow-up asking how delivery went is queued with the provider for a few days after dispatch.
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<CatalogItem> _itemRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderProgress> _orderProgressRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ISmsGateway _smsGateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> itemRepository,
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderProgress> orderProgressRepository,
        IRepository<OrderNotification> notificationRepository,
        ISmsGateway smsGateway,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _contactNumberRepository = contactNumberRepository;
        _orderProgressRepository = orderProgressRepository;
        _notificationRepository = notificationRepository;
        _smsGateway = smsGateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IEnumerable<OrderItemInput> items, ShippingAddressInput? shippingAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var requested = items?.ToList() ?? new List<OrderItemInput>();
        if (requested.Count == 0)
            throw new InvalidContactNumberException("An order must contain at least one item.");
        if (requested.Any(i => i.Quantity <= 0))
            throw new InvalidContactNumberException("Every item quantity must be greater than zero.");

        var catalogItemIds = requested.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);

        var orderItems = new List<OrderItem>();
        foreach (var input in requested)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == input.CatalogItemId);
            if (catalogItem is null)
                throw new NotificationEntityNotFoundException($"Catalog item {input.CatalogItemId} was not found.");

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, input.Quantity));
        }

        var address = shippingAddress is null
            ? new Address("N/A", "N/A", "N/A", "N/A", "N/A")
            : new Address(shippingAddress.Street, shippingAddress.City, shippingAddress.State, shippingAddress.Country, shippingAddress.ZipCode);

        var order = new Order(buyerId, address, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);

        await _orderProgressRepository.AddAsync(new OrderProgress(order.Id, buyerId), cancellationToken);

        _logger.LogInformation("Placed order {OrderId} for buyer {BuyerId}.", order.Id, buyerId);

        await NotifyAsync(order.Id, buyerId, OrderNotificationType.OrderPlaced,
            $"eShop: thanks! Your order #{order.Id} has been placed.", cancellationToken);

        return order.Id;
    }

    public async Task DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var progress = await LoadProgressAsync(orderId, cancellationToken);
        progress.MarkDispatched();
        await _orderProgressRepository.UpdateAsync(progress, cancellationToken);
        _logger.LogInformation("Order {OrderId} marked dispatched.", orderId);

        await NotifyAsync(orderId, progress.BuyerId, OrderNotificationType.OrderDispatched,
            $"eShop: good news - your order #{orderId} is on its way!", cancellationToken);

        // Queue a delivery follow-up with the provider for a few days later - the provider holds and sends it.
        await ScheduleFollowUpsAsync(orderId, progress.BuyerId,
            $"eShop: how did the delivery of your order #{orderId} go? We'd love your feedback.",
            DateTimeOffset.UtcNow.Add(FollowUpDelay), cancellationToken);
    }

    public async Task CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var progress = await LoadProgressAsync(orderId, cancellationToken);
        progress.MarkCancelled();
        await _orderProgressRepository.UpdateAsync(progress, cancellationToken);
        _logger.LogInformation("Order {OrderId} marked cancelled.", orderId);

        // Call off any not-yet-sent follow-up first: a "how did delivery go?" for a cancelled order
        // is exactly the incident this prevents.
        await CancelScheduledFollowUpsAsync(orderId, cancellationToken);

        await NotifyAsync(orderId, progress.BuyerId, OrderNotificationType.OrderCancelled,
            $"eShop: your order #{orderId} has been cancelled.", cancellationToken);
    }

    public async Task<IReadOnlyList<OrderSummary>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        if (orders.Count == 0)
            return new List<OrderSummary>();

        var orderIds = orders.Select(o => o.Id).ToList();
        var progresses = await _orderProgressRepository.ListAsync(new OrderProgressByOrderIdsSpecification(orderIds), cancellationToken);
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderIdsSpecification(orderIds), cancellationToken);

        await RefreshDeliveryStatesAsync(notifications, cancellationToken);

        var summaries = new List<OrderSummary>();
        foreach (var order in orders.OrderByDescending(o => o.OrderDate))
        {
            var status = progresses.FirstOrDefault(p => p.OrderId == order.Id)?.Status.ToString() ?? OrderProgressStatus.Placed.ToString();
            var views = notifications.Where(n => n.OrderId == order.Id).Select(ToView).ToList();
            summaries.Add(new OrderSummary(order.Id, status, order.OrderDate, order.Total(), views));
        }

        return summaries;
    }

    public async Task<IReadOnlyList<NotificationView>> GetOrderNotificationsAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        // Another shopper's order is not visible - report it as not found rather than leaking its existence.
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            throw new NotificationEntityNotFoundException($"Order {orderId} was not found.");

        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderIdSpecification(orderId), cancellationToken);
        await RefreshDeliveryStatesAsync(notifications, cancellationToken);
        return notifications.Select(ToView).ToList();
    }

    public async Task<int> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new InvalidContactNumberException("An idempotency key is required.");

        // Idempotent replay: a repeat under the same key returns the message the first attempt produced,
        // and does not send a second message.
        var alreadyProduced = await _notificationRepository.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (alreadyProduced is not null)
        {
            _logger.LogInformation("Resend idempotency key already used; returning notification {NotificationId}.", alreadyProduced.Id);
            return alreadyProduced.Id;
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
            throw new NotificationEntityNotFoundException($"Notification {notificationId} was not found.");

        // Make sure we act on the true current outcome before deciding whether a resend is warranted.
        await RefreshDeliveryStateAsync(original, cancellationToken);
        if (!NotificationStatuses.DidNotReachRecipient(original.Status))
            throw new OrderNotificationConflictException(
                $"Notification {notificationId} has status '{original.Status}' and does not need re-sending.");

        // The number must still be on file for the shopper - a removed number must never be messaged again.
        var stillRegistered = await _contactNumberRepository.FirstOrDefaultAsync(
            new ContactNumberByValueForBuyerSpecification(original.BuyerId, original.ToNumber), cancellationToken);
        if (stillRegistered is null)
            throw new OrderNotificationConflictException(
                $"The destination for notification {notificationId} is no longer on file, so it cannot be re-sent.");

        var body = original.Content ?? BuildBodyFor(original.Type, original.OrderId);

        // Persist the new message (with its idempotency key) before sending, so a concurrent repeat of the
        // same key finds it rather than sending again.
        var resend = new OrderNotification(original.OrderId, original.BuyerId, original.Type, original.ToNumber, body);
        resend.SetIdempotencyKey(idempotencyKey);
        await _notificationRepository.AddAsync(resend, cancellationToken);

        try
        {
            var msg = await _smsGateway.SendAsync(original.ToNumber, body, cancellationToken);
            resend.RecordProviderAccepted(msg.Sid, msg.Status, msg.ErrorCode, msg.ErrorMessage, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Resend of notification {NotificationId} could not be sent: {Error}", notificationId, ex.Message);
            resend.RecordNotSent(ex.Message);
        }

        await _notificationRepository.UpdateAsync(resend, cancellationToken);
        return resend.Id;
    }

    public async Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
            throw new NotificationEntityNotFoundException($"Notification {notificationId} was not found.");

        // Dispose of the text at the provider so it can no longer be retrieved there. This must succeed
        // for the disposal to be real - it is not swallowed like a best-effort send.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
            await _smsGateway.RedactContentAsync(notification.ProviderMessageSid!, cancellationToken);

        // The fact a message was sent, and what became of it, survives; only the content is disposed.
        notification.MarkContentDisposed();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Disposed of content for notification {NotificationId}.", notificationId);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
            throw new InvalidContactNumberException("'to' must not be earlier than 'from'.");

        // Ask the provider for its own record of messages from our configured sending number for the range.
        var providerMessages = await _smsGateway.ListMessagesFromConfiguredNumberAsync(from, to, cancellationToken);

        // What eShop believes it sent in the same range.
        var eshopSent = await _notificationRepository.ListAsync(new SentOrderNotificationsInRangeSpecification(from, to), cancellationToken);

        var eshopBySid = eshopSent
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());
        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var eShopOnly = new List<ReconciliationEntry>();

        foreach (var msg in providerBySid.Values)
        {
            if (eshopBySid.TryGetValue(msg.Sid, out var local))
                matched.Add(new ReconciliationEntry(msg.Sid, msg.Status, local.Id, local.OrderId));
            else
                providerOnly.Add(new ReconciliationEntry(msg.Sid, msg.Status, null, null));
        }

        foreach (var local in eshopBySid.Values)
        {
            if (!providerBySid.ContainsKey(local.ProviderMessageSid!))
                eShopOnly.Add(new ReconciliationEntry(local.ProviderMessageSid, local.Status, local.Id, local.OrderId));
        }

        return new ReconciliationReport(
            from, to, _smsGateway.FromNumber,
            providerBySid.Count, eshopBySid.Count, matched.Count,
            matched, providerOnly, eShopOnly);
    }

    // ----- helpers -------------------------------------------------------------------------------

    private async Task<OrderProgress> LoadProgressAsync(int orderId, CancellationToken cancellationToken)
    {
        var progress = await _orderProgressRepository.FirstOrDefaultAsync(new OrderProgressByOrderIdSpecification(orderId), cancellationToken);
        if (progress is null)
            throw new NotificationEntityNotFoundException($"Order {orderId} was not found.");
        return progress;
    }

    private async Task NotifyAsync(int orderId, string buyerId, OrderNotificationType type, string body, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        // A shopper with no number on file is simply not messaged.
        foreach (var contactNumber in numbers)
        {
            var notification = new OrderNotification(orderId, buyerId, type, contactNumber.PhoneNumber, body);
            try
            {
                var msg = await _smsGateway.SendAsync(contactNumber.PhoneNumber, body, cancellationToken);
                notification.RecordProviderAccepted(msg.Sid, msg.Status, msg.ErrorCode, msg.ErrorMessage, null);
            }
            catch (Exception ex)
            {
                // A message that cannot be sent must never fail the underlying operation.
                _logger.LogWarning("Order {OrderId} notification ({Type}) could not be sent: {Error}", orderId, type, ex.Message);
                notification.RecordNotSent(ex.Message);
            }

            await _notificationRepository.AddAsync(notification, cancellationToken);
        }
    }

    private async Task ScheduleFollowUpsAsync(int orderId, string buyerId, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        foreach (var contactNumber in numbers)
        {
            var notification = new OrderNotification(orderId, buyerId, OrderNotificationType.DeliveryFollowUp, contactNumber.PhoneNumber, body);
            try
            {
                var msg = await _smsGateway.ScheduleAsync(contactNumber.PhoneNumber, body, sendAt, cancellationToken);
                notification.RecordProviderAccepted(msg.Sid, msg.Status, msg.ErrorCode, msg.ErrorMessage, sendAt);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Order {OrderId} delivery follow-up could not be scheduled: {Error}", orderId, ex.Message);
                notification.RecordNotSent(ex.Message);
            }

            await _notificationRepository.AddAsync(notification, cancellationToken);
        }
    }

    private async Task CancelScheduledFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notificationRepository.ListAsync(new ScheduledFollowUpsByOrderIdSpecification(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            try
            {
                await _smsGateway.CancelScheduledAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.MarkScheduledCancelled();
                await _notificationRepository.UpdateAsync(followUp, cancellationToken);
                _logger.LogInformation("Called off scheduled follow-up {NotificationId} for order {OrderId}.", followUp.Id, orderId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not call off scheduled follow-up {NotificationId} for order {OrderId}: {Error}", followUp.Id, orderId, ex.Message);
            }
        }
    }

    private async Task RefreshDeliveryStatesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
            await RefreshDeliveryStateAsync(notification, cancellationToken);
    }

    private async Task RefreshDeliveryStateAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            return;
        if (NotificationStatuses.IsTerminal(notification.Status))
            return;

        try
        {
            var state = await _smsGateway.FetchAsync(notification.ProviderMessageSid!, cancellationToken);
            if (!string.Equals(state.Status, notification.Status, StringComparison.Ordinal)
                || state.ErrorCode != notification.ProviderErrorCode)
            {
                notification.UpdateDeliveryState(state.Status, state.ErrorCode, state.ErrorMessage);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not refresh delivery state for notification {NotificationId}: {Error}", notification.Id, ex.Message);
        }
    }

    private static string BuildBodyFor(OrderNotificationType type, int orderId) => type switch
    {
        OrderNotificationType.OrderPlaced => $"eShop: thanks! Your order #{orderId} has been placed.",
        OrderNotificationType.OrderDispatched => $"eShop: good news - your order #{orderId} is on its way!",
        OrderNotificationType.OrderCancelled => $"eShop: your order #{orderId} has been cancelled.",
        OrderNotificationType.DeliveryFollowUp => $"eShop: how did the delivery of your order #{orderId} go? We'd love your feedback.",
        _ => $"eShop: an update about your order #{orderId}."
    };

    private static NotificationView ToView(OrderNotification n) => new(
        n.Id, n.OrderId, n.Type.ToString(), n.ProviderMessageSid, n.Status,
        n.ProviderErrorCode, n.ProviderErrorMessage, n.ContentDisposed, n.Content, n.CreatedDate, n.ScheduledFor);
}
