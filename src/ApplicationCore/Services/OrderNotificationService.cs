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
    /// <summary>How far ahead the delivery-feedback follow-up is queued with the provider.</summary>
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<Notification> _notificationRepository;
    private readonly IReadRepository<ContactNumber> _contactNumberRepository;
    private readonly ITwilioMessagingGateway _messagingGateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> catalogItemRepository,
        IRepository<Notification> notificationRepository,
        IReadRepository<ContactNumber> contactNumberRepository,
        ITwilioMessagingGateway messagingGateway,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _catalogItemRepository = catalogItemRepository;
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _messagingGateway = messagingGateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<PlaceOrderResult> PlaceOrderAsync(
        string buyerId, IReadOnlyList<OrderLine> lines, ShippingAddress? shippingAddress, CancellationToken cancellationToken)
    {
        if (lines is null || lines.Count == 0)
        {
            return new PlaceOrderResult(false, 0, "At least one order line is required.");
        }

        if (lines.Any(l => l.Quantity <= 0))
        {
            return new PlaceOrderResult(false, 0, "Each order line must have a quantity greater than zero.");
        }

        var itemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItemRepository.ListAsync(new CatalogItemsSpecification(itemIds), cancellationToken);

        var missing = itemIds.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            return new PlaceOrderResult(false, 0, $"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var address = shippingAddress is null
            ? new Address("N/A", "N/A", "N/A", "N/A", "00000")
            : new Address(shippingAddress.Street, shippingAddress.City, shippingAddress.State, shippingAddress.Country, shippingAddress.ZipCode);

        var order = new Order(buyerId, address, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);

        _logger.LogInformation("Placed order {OrderId} for buyer {BuyerId}.", order.Id, buyerId);

        // Notifying must never fail the placement.
        await SafeSendImmediateAsync(order, NotificationKind.OrderPlaced, BuildOrderPlacedBody(order), cancellationToken);

        return new PlaceOrderResult(true, order.Id, null);
    }

    public async Task<OrderActionResult> DispatchAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return new OrderActionResult(OrderActionStatus.NotFound, $"Order {orderId} was not found.");
        }

        try
        {
            order.MarkAsDispatched();
        }
        catch (InvalidOrderStateException ex)
        {
            return new OrderActionResult(OrderActionStatus.InvalidState, ex.Message);
        }

        // Commit the state change before doing anything with the provider — the dispatch stands on its own.
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Dispatched order {OrderId}.", orderId);

        // Tell the shopper it is on its way, and queue the "how did delivery go?" follow-up with the provider.
        await SafeSendImmediateAsync(order, NotificationKind.OrderDispatched, BuildOrderDispatchedBody(order), cancellationToken);
        await SafeScheduleFollowUpAsync(order, cancellationToken);

        return new OrderActionResult(OrderActionStatus.Success, null);
    }

    public async Task<OrderActionResult> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return new OrderActionResult(OrderActionStatus.NotFound, $"Order {orderId} was not found.");
        }

        try
        {
            order.MarkAsCancelled();
        }
        catch (InvalidOrderStateException ex)
        {
            return new OrderActionResult(OrderActionStatus.InvalidState, ex.Message);
        }

        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Cancelled order {OrderId}.", orderId);

        // A follow-up that has not gone out must never reach the customer — call it off first.
        await SafeCancelPendingFollowUpsAsync(order, cancellationToken);
        await SafeSendImmediateAsync(order, NotificationKind.OrderCancelled, BuildOrderCancelledBody(order), cancellationToken);

        return new OrderActionResult(OrderActionStatus.Success, null);
    }

    public async Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _orderRepository.ListAsync(new OrdersByBuyerSpecification(buyerId), cancellationToken);
        var notifications = await _notificationRepository.ListAsync(new NotificationsByBuyerSpecification(buyerId), cancellationToken);

        await RefreshDeliveryStatusesAsync(notifications, cancellationToken);

        var notificationsByOrder = notifications
            .GroupBy(n => n.OrderId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Notification>)g.OrderBy(n => n.CreatedAt).ToList());

        return orders
            .Select(o => new OrderWithNotifications(
                o,
                notificationsByOrder.TryGetValue(o.Id, out var list) ? list : Array.Empty<Notification>()))
            .ToList();
    }

    public async Task<IReadOnlyList<Notification>?> GetOrderNotificationsAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        // Scope to the caller's own order.
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderByIdForBuyerSpecification(orderId, buyerId), cancellationToken);
        if (order is null)
        {
            return null;
        }

        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshDeliveryStatusesAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<ResendResult?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        // Idempotency: a repeat under the same key returns the earlier result without sending again.
        var alreadyDone = await _notificationRepository.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (alreadyDone is not null)
        {
            return new ResendResult(alreadyDone.Id, true);
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            return null;
        }

        // Persist the new message (carrying the idempotency key) BEFORE sending, so a concurrent repeat
        // under the same key is deduplicated rather than sending twice.
        var resend = new Notification(
            original.OrderId, original.BuyerId, original.Kind, original.ToNumber, original.Body,
            idempotencyKey: idempotencyKey, resendOfNotificationId: original.Id);
        await _notificationRepository.AddAsync(resend, cancellationToken);

        try
        {
            var state = await _messagingGateway.SendSmsAsync(resend.ToNumber, resend.Body, cancellationToken);
            resend.MarkSent(state.Sid, state.Status, state.ErrorCode, state.ErrorMessage);
        }
        catch (Exception ex)
        {
            resend.MarkSendFailed(ex.Message);
            _logger.LogWarning("Re-send of notification {NotificationId} (as {NewId}) could not be delivered to the provider.",
                original.Id, resend.Id);
        }

        await _notificationRepository.UpdateAsync(resend, cancellationToken);
        return new ResendResult(resend.Id, false);
    }

    public async Task<bool?> DisposeContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return null;
        }

        // Redact at the provider first so the text is genuinely gone there; only then clear it locally.
        // A provider failure propagates so the caller learns the content was NOT disposed of.
        if (notification.ProviderMessageSid is not null && !notification.ContentRedacted)
        {
            await _messagingGateway.RedactMessageBodyAsync(notification.ProviderMessageSid, cancellationToken);
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Disposed of content for notification {NotificationId}.", notificationId);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var providerMessages = await _messagingGateway.ListMessagesAsync(from, to, cancellationToken);
        var localNotifications = await _notificationRepository.ListAsync(new SentNotificationsInRangeSpecification(from, to), cancellationToken);

        var localBySid = localNotifications
            .Where(n => n.ProviderMessageSid is not null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var providerSids = new HashSet<string>(
            providerMessages.Where(m => m.Sid is not null).Select(m => m.Sid!));

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();

        foreach (var message in providerMessages)
        {
            if (message.Sid is not null && localBySid.TryGetValue(message.Sid, out var local))
            {
                matched.Add(new ReconciliationEntry(message.Sid, message.Status, local.Status, local.Id, local.OrderId));
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry(message.Sid, message.Status, null, null, null));
            }
        }

        var eShopOnly = localBySid.Values
            .Where(n => !providerSids.Contains(n.ProviderMessageSid!))
            .Select(n => new ReconciliationEntry(n.ProviderMessageSid, null, n.Status, n.Id, n.OrderId))
            .ToList();

        return new ReconciliationReport(
            from, to, _messagingGateway.SendingNumber,
            providerMessages.Count, localBySid.Count, matched.Count,
            matched, providerOnly, eShopOnly);
    }

    // --- messaging helpers: none of these ever throw to the caller ---

    private async Task SafeSendImmediateAsync(Order order, NotificationKind kind, string body, CancellationToken cancellationToken)
    {
        try
        {
            var toNumber = await GetActiveContactNumberAsync(order.BuyerId, cancellationToken);
            if (toNumber is null)
            {
                // A shopper with no number on file is simply not messaged.
                return;
            }

            var notification = new Notification(order.Id, order.BuyerId, kind, toNumber, body);
            await _notificationRepository.AddAsync(notification, cancellationToken);

            try
            {
                var state = await _messagingGateway.SendSmsAsync(toNumber, body, cancellationToken);
                notification.MarkSent(state.Sid, state.Status, state.ErrorCode, state.ErrorMessage);
            }
            catch (Exception ex)
            {
                notification.MarkSendFailed(ex.Message);
                _logger.LogWarning("Notification {NotificationId} for order {OrderId} could not be delivered to the provider.",
                    notification.Id, order.Id);
            }

            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception)
        {
            // Never let a messaging problem fail the underlying order operation.
            _logger.LogWarning("An unexpected error occurred while notifying for order {OrderId}; the order operation is unaffected.", order.Id);
        }
    }

    private async Task SafeScheduleFollowUpAsync(Order order, CancellationToken cancellationToken)
    {
        try
        {
            var toNumber = await GetActiveContactNumberAsync(order.BuyerId, cancellationToken);
            if (toNumber is null)
            {
                return;
            }

            var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
            var body = BuildFollowUpBody(order);
            var notification = new Notification(order.Id, order.BuyerId, NotificationKind.DeliveryFollowUp, toNumber, body,
                isScheduled: true, scheduledSendAt: sendAt);
            await _notificationRepository.AddAsync(notification, cancellationToken);

            try
            {
                var state = await _messagingGateway.ScheduleSmsAsync(toNumber, body, sendAt, cancellationToken);
                notification.MarkScheduled(state.Sid, state.Status);
                _logger.LogInformation("Queued a delivery follow-up (notification {NotificationId}) for order {OrderId}.",
                    notification.Id, order.Id);
            }
            catch (Exception)
            {
                notification.MarkSendFailed("The delivery follow-up could not be scheduled with the provider.");
                _logger.LogWarning("Could not schedule a delivery follow-up for order {OrderId}.", order.Id);
            }

            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogWarning("An unexpected error occurred while scheduling a follow-up for order {OrderId}; the order operation is unaffected.", order.Id);
        }
    }

    private async Task SafeCancelPendingFollowUpsAsync(Order order, CancellationToken cancellationToken)
    {
        try
        {
            var pending = await _notificationRepository.ListAsync(new PendingFollowUpsByOrderSpecification(order.Id), cancellationToken);
            foreach (var followUp in pending)
            {
                try
                {
                    var state = await _messagingGateway.CancelScheduledMessageAsync(followUp.ProviderMessageSid!, cancellationToken);
                    followUp.MarkCancelled(state.Status);
                    _logger.LogInformation("Called off the pending follow-up (notification {NotificationId}) for order {OrderId}.",
                        followUp.Id, order.Id);
                }
                catch (Exception)
                {
                    _logger.LogWarning("Could not cancel the pending follow-up (notification {NotificationId}) for order {OrderId}.",
                        followUp.Id, order.Id);
                }

                await _notificationRepository.UpdateAsync(followUp, cancellationToken);
            }
        }
        catch (Exception)
        {
            _logger.LogWarning("An unexpected error occurred while cancelling follow-ups for order {OrderId}; the order operation is unaffected.", order.Id);
        }
    }

    private async Task RefreshDeliveryStatusesAsync(IReadOnlyList<Notification> notifications, CancellationToken cancellationToken)
    {
        // There is no callback URL, so the only way to learn a message's fate is to ask the provider.
        foreach (var notification in notifications)
        {
            if (notification.ProviderMessageSid is null || NotificationStatus.IsTerminal(notification.Status))
            {
                continue;
            }

            try
            {
                var state = await _messagingGateway.GetMessageStateAsync(notification.ProviderMessageSid, cancellationToken);
                if (state.Status != notification.Status || state.ErrorCode != notification.ErrorCode)
                {
                    notification.UpdateDeliveryStatus(state.Status, state.ErrorCode, state.ErrorMessage);
                    await _notificationRepository.UpdateAsync(notification, cancellationToken);
                }
            }
            catch (Exception)
            {
                // A refresh failure must not fail the read; keep the last-known status.
                _logger.LogWarning("Could not refresh delivery status for notification {NotificationId}.", notification.Id);
            }
        }
    }

    private async Task<string?> GetActiveContactNumberAsync(string buyerId, CancellationToken cancellationToken)
    {
        // Message the shopper's most recently registered number.
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers.Count == 0 ? null : numbers[0].PhoneNumber;
    }

    private static string BuildOrderPlacedBody(Order order) =>
        $"eShop: your order #{order.Id} has been placed. Total {order.Total():C}. Thank you for shopping with us!";

    private static string BuildOrderDispatchedBody(Order order) =>
        $"eShop: good news! Your order #{order.Id} is on its way.";

    private static string BuildFollowUpBody(Order order) =>
        $"eShop: how did the delivery of your order #{order.Id} go? We'd love your feedback.";

    private static string BuildOrderCancelledBody(Order order) =>
        $"eShop: your order #{order.Id} has been cancelled. If this is unexpected, please contact support.";
}
