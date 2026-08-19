using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the order-notification flows on top of the existing order model and the messaging
/// gateway. Sending a message is always best-effort: a send that fails is recorded but never fails
/// the order operation that triggered it.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    // How far ahead the "how did the delivery go?" follow-up is queued with the provider.
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ISmsGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ISmsGateway gateway,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<int> PlaceOrderAsync(string ownerId, IReadOnlyList<OrderLine> lines, ShippingAddress? address, CancellationToken cancellationToken = default)
    {
        var itemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(itemIds), cancellationToken);

        var missing = itemIds.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            throw new NotFoundException($"No catalog item exists with id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var shipTo = address is null
            ? new Address("N/A", "N/A", "N/A", "N/A", "N/A")
            : new Address(address.Street, address.City, address.State, address.Country, address.ZipCode);

        var order = new Order(ownerId, shipTo, orderItems);
        await _orders.AddAsync(order, cancellationToken);

        await SendImmediateAsync(order, NotificationKind.OrderPlaced, PlacedBody(order.Id), cancellationToken);

        return order.Id;
    }

    public async Task DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            throw new NotFoundException($"No order exists with id {orderId}.");
        }

        try
        {
            order.MarkDispatched();
        }
        catch (InvalidOperationException ex)
        {
            throw new NotificationOperationException(ex.Message);
        }

        // Persist the state change first so a messaging failure can never undo it.
        await _orders.UpdateAsync(order, cancellationToken);

        await SendImmediateAsync(order, NotificationKind.OrderDispatched, DispatchedBody(order.Id), cancellationToken);
        await ScheduleFollowUpAsync(order, cancellationToken);
    }

    public async Task CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            throw new NotFoundException($"No order exists with id {orderId}.");
        }

        try
        {
            order.MarkCancelled();
        }
        catch (InvalidOperationException ex)
        {
            throw new NotificationOperationException(ex.Message);
        }

        await _orders.UpdateAsync(order, cancellationToken);

        // Call off any follow-up that has not yet gone out — a cancelled order must never get a
        // "how did the delivery go?" message.
        await CancelPendingFollowUpsAsync(orderId, cancellationToken);

        await SendImmediateAsync(order, NotificationKind.OrderCancelled, CancelledBody(order.Id), cancellationToken);
    }

    public async Task<IReadOnlyList<OrderSummaryView>> GetMyOrdersAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(ownerId), cancellationToken);
        var notifications = await _notifications.ListAsync(new OrderNotificationsByOwnerSpecification(ownerId), cancellationToken);
        await RefreshDeliveryStateAsync(notifications, cancellationToken);

        var byOrder = notifications.ToLookup(n => n.OrderId);

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => new OrderSummaryView(
                o.Id,
                o.OrderDate,
                o.Total(),
                o.Status.ToString(),
                byOrder[o.Id].Select(ToView).ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<NotificationView>?> GetOrderNotificationsAsync(int orderId, string ownerId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null || order.BuyerId != ownerId)
        {
            // Owner-scoped: another shopper's order is indistinguishable from a non-existent one.
            return null;
        }

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshDeliveryStateAsync(notifications, cancellationToken);
        return notifications.Select(ToView).ToList();
    }

    public async Task<int> ResendNotificationAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // Idempotency: a repeat under the same key returns the message the first attempt produced.
        var prior = await _notifications.FirstOrDefaultAsync(new OrderNotificationByResendKeySpecification(idempotencyKey), cancellationToken);
        if (prior is not null)
        {
            return prior.Id;
        }

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (source is null)
        {
            throw new NotFoundException($"No notification exists with id {notificationId}.");
        }

        if (source.ContentDisposed || source.Body is null)
        {
            throw new NotificationOperationException($"Notification {notificationId} has had its content disposed of and cannot be re-sent.");
        }

        // Confirm the message really did not reach the shopper before re-sending.
        if (source.ProviderMessageSid is not null && !MessageDeliveryStatus.IsTerminal(source.Status))
        {
            await RefreshOneAsync(source, cancellationToken);
        }

        if (Reached(source.Status))
        {
            throw new NotificationOperationException(
                $"Notification {notificationId} was '{source.Status}' and reached the shopper; re-send is not applicable.");
        }

        var resend = OrderNotification.ForOrder(source.OrderId, source.OwnerId, source.Kind, source.ToNumber, source.Body);
        resend.MarkResendOf(source.Id, idempotencyKey);
        // Add first so the idempotency key is recorded even if the send throws.
        await _notifications.AddAsync(resend, cancellationToken);

        try
        {
            var message = await _gateway.SendAsync(source.ToNumber, source.Body, cancellationToken);
            resend.RecordAccepted(message.Sid, message.Status ?? MessageDeliveryStatus.Queued, message.ErrorCode, message.ErrorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Order {source.OrderId}: re-send of notification {notificationId} could not be sent: {ex.Message}");
            resend.RecordNotSent(ex.Message);
        }

        await _notifications.UpdateAsync(resend, cancellationToken);
        return resend.Id;
    }

    public async Task DisposeNotificationContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            throw new NotFoundException($"No notification exists with id {notificationId}.");
        }

        if (notification.ContentDisposed)
        {
            return; // Already disposed of; nothing further to do.
        }

        if (notification.ProviderMessageSid is not null)
        {
            // Redact at the provider first. Only clear our own copy once the provider confirms,
            // so we never claim the text is gone while it still lives at the provider.
            try
            {
                await _gateway.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
            }
            catch (Exception ex)
            {
                throw new NotificationOperationException($"Could not dispose of the message content at the provider: {ex.Message}");
            }
        }

        notification.MarkContentDisposed();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider only for the configured sender's messages in the range.
        var providerMessages = await _gateway.ListSentByConfiguredSenderAsync(from, to, cancellationToken);
        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First());

        // What eShop believes it sent in the range: notifications it handed a provider SID.
        var allNotifications = await _notifications.ListAsync(cancellationToken);
        var eShopBySid = allNotifications
            .Where(n => n.ProviderMessageSid is not null && n.CreatedDate >= from && n.CreatedDate <= to)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var eShopOnly = new List<ReconciliationEntry>();

        foreach (var (sid, message) in providerBySid)
        {
            if (eShopBySid.TryGetValue(sid, out var notification))
            {
                matched.Add(new ReconciliationEntry(sid, message.Status, message.DateSent,
                    notification.Id, notification.OrderId, notification.Kind.ToString(), notification.Status));
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry(sid, message.Status, message.DateSent, null, null, null, null));
            }
        }

        foreach (var (sid, notification) in eShopBySid)
        {
            if (!providerBySid.ContainsKey(sid))
            {
                eShopOnly.Add(new ReconciliationEntry(sid, null, null,
                    notification.Id, notification.OrderId, notification.Kind.ToString(), notification.Status));
            }
        }

        return new ReconciliationReport(
            from, to, _gateway.ConfiguredSender,
            matched.Count, providerOnly.Count, eShopOnly.Count,
            matched, providerOnly, eShopOnly);
    }

    // --- helpers ---------------------------------------------------------

    private async Task SendImmediateAsync(Order order, NotificationKind kind, string body, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(order.BuyerId), cancellationToken);
        // No number on file: the shopper is simply not messaged.
        foreach (var number in numbers)
        {
            var notification = OrderNotification.ForOrder(order.Id, order.BuyerId, kind, number.E164Number, body);
            try
            {
                var message = await _gateway.SendAsync(number.E164Number, body, cancellationToken);
                notification.RecordAccepted(message.Sid, message.Status ?? MessageDeliveryStatus.Queued, message.ErrorCode, message.ErrorMessage);
            }
            catch (Exception ex)
            {
                // A message that cannot be sent must never fail the order operation.
                _logger.LogWarning($"Order {order.Id}: {kind} SMS could not be sent: {ex.Message}");
                notification.RecordNotSent(ex.Message);
            }

            await _notifications.AddAsync(notification, cancellationToken);
        }
    }

    private async Task ScheduleFollowUpAsync(Order order, CancellationToken cancellationToken)
    {
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(order.BuyerId), cancellationToken);
        foreach (var number in numbers)
        {
            var body = FollowUpBody(order.Id);
            var notification = OrderNotification.ForOrder(order.Id, order.BuyerId, NotificationKind.DeliveryFollowUp, number.E164Number, body);
            notification.MarkScheduled(sendAt);
            try
            {
                var message = await _gateway.ScheduleAsync(number.E164Number, body, sendAt, cancellationToken);
                notification.RecordAccepted(message.Sid, message.Status ?? MessageDeliveryStatus.Scheduled, message.ErrorCode, message.ErrorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Order {order.Id}: delivery follow-up could not be scheduled: {ex.Message}");
                notification.RecordNotSent(ex.Message);
            }

            await _notifications.AddAsync(notification, cancellationToken);
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        foreach (var notification in notifications.Where(IsCancellableFollowUp))
        {
            try
            {
                var message = await _gateway.CancelScheduledAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.UpdateDeliveryState(message.Status ?? MessageDeliveryStatus.Canceled, message.ErrorCode, message.ErrorMessage);
            }
            catch (Exception ex)
            {
                // If it could not be cancelled (e.g. it already went out), refresh its real state so
                // the record reflects what actually happened rather than silently swallowing it.
                _logger.LogWarning($"Order {orderId}: scheduled follow-up could not be cancelled: {ex.Message}");
                await RefreshOneAsync(notification, cancellationToken);
            }

            await _notifications.UpdateAsync(notification, cancellationToken);
        }
    }

    private static bool IsCancellableFollowUp(OrderNotification n) =>
        n.Kind == NotificationKind.DeliveryFollowUp
        && n.IsScheduled
        && n.ProviderMessageSid is not null
        && !MessageDeliveryStatus.IsTerminal(n.Status);

    private async Task RefreshDeliveryStateAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (notification.ProviderMessageSid is null
                || notification.ContentDisposed
                || MessageDeliveryStatus.IsTerminal(notification.Status))
            {
                continue;
            }

            if (await RefreshOneAsync(notification, cancellationToken))
            {
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
        }
    }

    private async Task<bool> RefreshOneAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (notification.ProviderMessageSid is null)
        {
            return false;
        }

        try
        {
            var message = await _gateway.GetAsync(notification.ProviderMessageSid, cancellationToken);
            if (message.Status is not null)
            {
                notification.UpdateDeliveryState(message.Status, message.ErrorCode, message.ErrorMessage);
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Order {notification.OrderId}: could not refresh delivery state for notification {notification.Id}: {ex.Message}");
        }

        return false;
    }

    private static bool Reached(string? status) =>
        string.Equals(status, MessageDeliveryStatus.Delivered, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, MessageDeliveryStatus.Read, StringComparison.OrdinalIgnoreCase);

    private static NotificationView ToView(OrderNotification n) => new(
        n.Id, n.OrderId, n.Kind.ToString(), n.Status, n.ErrorCode, n.ErrorMessage,
        n.IsScheduled, n.ScheduledFor, n.ContentDisposed, n.Body is not null && !n.ContentDisposed,
        n.ProviderMessageSid, n.ResendOfNotificationId, n.CreatedDate);

    private static string PlacedBody(int orderId) => $"eShopOnWeb: thanks! Your order #{orderId} has been placed.";
    private static string DispatchedBody(int orderId) => $"eShopOnWeb: good news — your order #{orderId} is on its way.";
    private static string FollowUpBody(int orderId) => $"eShopOnWeb: how did the delivery of your order #{orderId} go? We'd love your feedback.";
    private static string CancelledBody(int orderId) => $"eShopOnWeb: your order #{orderId} has been cancelled.";
}
