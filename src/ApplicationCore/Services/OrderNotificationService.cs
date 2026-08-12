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
    /// <summary>How far after dispatch the "how did the delivery go?" follow-up is scheduled.</summary>
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private static readonly string[] FinalStatuses = { "delivered", "undelivered", "failed", "canceled", "read" };

    private readonly IRepository<Order> _orders;
    private readonly IReadRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ISmsGateway _smsGateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orders,
        IReadRepository<CatalogItem> catalogItems,
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ISmsGateway smsGateway,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _smsGateway = smsGateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> items,
        Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(items, nameof(items));
        if (items.Count == 0)
            throw new ArgumentException("An order must contain at least one item.", nameof(items));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var orderItems = new List<OrderItem>();
        foreach (var line in items)
        {
            if (line.Quantity <= 0)
                throw new ArgumentException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.", nameof(items));

            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new ArgumentException($"Catalog item {line.CatalogItemId} does not exist.", nameof(items));

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orders.AddAsync(order, cancellationToken);

        _logger.LogInformation("Order {OrderId} placed for buyer via PublicApi.", order.Id);

        await SendImmediateNotificationsAsync(order, NotificationType.OrderPlaced, cancellationToken);
        return order;
    }

    public async Task<Order?> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
            return null;

        order.Dispatch();
        await _orders.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Order {OrderId} marked dispatched.", order.Id);

        await SendImmediateNotificationsAsync(order, NotificationType.OrderDispatched, cancellationToken);
        await ScheduleFollowUpsAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
            return null;

        order.Cancel();
        await _orders.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Order {OrderId} cancelled.", order.Id);

        // Call off any delivery follow-up that is still scheduled with the provider, so a cancelled
        // order never later asks the shopper how their (non-existent) delivery went.
        await CancelScheduledFollowUpsAsync(order.Id, cancellationToken);

        await SendImmediateNotificationsAsync(order, NotificationType.OrderCancelled, cancellationToken);
        return order;
    }

    public async Task<IReadOnlyList<OrderWithNotifications>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var notifications = await _notifications.ListAsync(new OrderNotificationsByOwnerSpecification(buyerId), cancellationToken);

        await RefreshDeliveryStateAsync(notifications, cancellationToken);

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());
        return orders
            .Select(o => new OrderWithNotifications(o,
                byOrder.TryGetValue(o.Id, out var list)
                    ? list
                    : (IReadOnlyList<OrderNotification>)Array.Empty<OrderNotification>()))
            .ToList();
    }

    public async Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        // Owner-scoped: only the shopper who owns the order can view its notifications.
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null || order.BuyerId != buyerId)
            return null;

        var notifications = await _notifications.ListAsync(
            new OrderNotificationsByOwnerAndOrderSpecification(buyerId, orderId), cancellationToken);

        await RefreshDeliveryStateAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        // Idempotency: a repeat under the same key returns the message already produced and sends nothing more.
        var alreadyProduced = await _notifications.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (alreadyProduced is not null)
        {
            _logger.LogInformation("Resend under existing idempotency key returned notification {NotificationId} without re-sending.", alreadyProduced.Id);
            return alreadyProduced;
        }

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (source is null)
            return null;

        // Rebuild a fresh body from the order and message kind, so a resend still works even if the
        // original message's content has since been disposed of.
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(source.OrderId), cancellationToken);
        var body = order is not null
            ? NotificationMessageComposer.Compose(source.Type, order)
            : source.Body ?? $"eShop: Update on your order #{source.OrderId}.";

        var resend = OrderNotification.CreateImmediate(source.OwnerId, source.OrderId, source.Type,
            source.ToPhoneNumber, body, idempotencyKey, sourceNotificationId: source.Id);
        resend = await _notifications.AddAsync(resend, cancellationToken);

        await SubmitAsync(resend, cancellationToken);
        _logger.LogInformation("Resent notification {SourceId} as {NotificationId} (status {Status}).",
            source.Id, resend.Id, resend.ProviderStatus);
        return resend;
    }

    public async Task<bool> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
            return false;

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            // Redact at the provider first, so the text is no longer retrievable there either.
            await _smsGateway.RedactMessageBodyAsync(notification.ProviderMessageSid, cancellationToken);
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Content of notification {NotificationId} disposed of; record and delivery outcome retained.", notification.Id);
        return true;
    }

    public async Task<ReconciliationResult> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _smsGateway.ListSentMessagesAsync(from, to, cancellationToken);

        var allLocalWithSid = await _notifications.ListAsync(new OrderNotificationsWithProviderSidSpecification(), cancellationToken);
        var localBySid = allLocalWithSid
            .Where(n => n.ProviderMessageSid is not null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var providerSids = new HashSet<string>(providerMessages.Select(m => m.Sid));

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        foreach (var m in providerMessages)
        {
            if (localBySid.TryGetValue(m.Sid, out var local))
            {
                matched.Add(new ReconciliationEntry(m.Sid, local.Id, local.OrderId, m.Status, local.ProviderStatus, m.DateSent));
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry(m.Sid, null, null, m.Status, null, m.DateSent));
            }
        }

        // eShop believes it sent these (within the range) but the provider's record for the sending
        // number in this range does not include them.
        var eShopOnly = allLocalWithSid
            .Where(n => n.CreatedAt >= from && n.CreatedAt <= to)
            .Where(n => n.ProviderMessageSid is not null && !providerSids.Contains(n.ProviderMessageSid))
            .Select(n => new ReconciliationEntry(n.ProviderMessageSid, n.Id, n.OrderId, null, n.ProviderStatus, null))
            .ToList();

        return new ReconciliationResult(from, to, _smsGateway.SendingNumber, matched, providerOnly, eShopOnly);
    }

    // ----- helpers -----

    private async Task SendImmediateNotificationsAsync(Order order, NotificationType type, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(order.BuyerId), cancellationToken);
        if (numbers.Count == 0)
        {
            // A shopper with no number on file is simply not messaged.
            _logger.LogInformation("No contact number on file for order {OrderId}; skipping {Type} notification.", order.Id, type);
            return;
        }

        var body = NotificationMessageComposer.Compose(type, order);
        foreach (var number in numbers)
        {
            var notification = OrderNotification.CreateImmediate(order.BuyerId, order.Id, type, number.PhoneNumber, body);
            notification = await _notifications.AddAsync(notification, cancellationToken);
            await SubmitAsync(notification, cancellationToken);
        }
    }

    private async Task ScheduleFollowUpsAsync(Order order, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(order.BuyerId), cancellationToken);
        if (numbers.Count == 0)
            return;

        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var body = NotificationMessageComposer.Compose(NotificationType.DeliveryFollowUp, order);
        foreach (var number in numbers)
        {
            var notification = OrderNotification.CreateScheduled(order.BuyerId, order.Id,
                NotificationType.DeliveryFollowUp, number.PhoneNumber, body, sendAt);
            notification = await _notifications.AddAsync(notification, cancellationToken);

            try
            {
                var result = await _smsGateway.ScheduleSmsAsync(number.PhoneNumber, body, sendAt, cancellationToken);
                notification.RecordSubmission(result.MessageSid, result.Status);
                _logger.LogInformation("Scheduled delivery follow-up {NotificationId} for order {OrderId} (message {Sid}, status {Status}).",
                    notification.Id, order.Id, result.MessageSid, result.Status);
            }
            catch (Exception ex)
            {
                notification.RecordSubmissionFailure();
                _logger.LogWarning("Could not schedule delivery follow-up {NotificationId} for order {OrderId}: {Error}.",
                    notification.Id, order.Id, ex.Message);
            }

            await _notifications.UpdateAsync(notification, cancellationToken);
        }
    }

    private async Task CancelScheduledFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notifications.ListAsync(
            new OrderNotificationsByOrderAndTypeSpecification(orderId, NotificationType.DeliveryFollowUp), cancellationToken);

        foreach (var followUp in followUps)
        {
            if (string.IsNullOrEmpty(followUp.ProviderMessageSid) || followUp.ProviderStatus != "scheduled")
                continue;

            try
            {
                await _smsGateway.CancelScheduledMessageAsync(followUp.ProviderMessageSid, cancellationToken);
                followUp.MarkCanceled();
                _logger.LogInformation("Called off scheduled follow-up {NotificationId} (message {Sid}) for cancelled order {OrderId}.",
                    followUp.Id, followUp.ProviderMessageSid, orderId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not call off scheduled follow-up {NotificationId} for order {OrderId}: {Error}.",
                    followUp.Id, orderId, ex.Message);
            }

            await _notifications.UpdateAsync(followUp, cancellationToken);
        }
    }

    /// <summary>
    /// Hands an already-persisted immediate notification to the provider, recording the outcome.
    /// A send failure is recorded on the notification and never propagates: the caller's operation
    /// (place/dispatch/cancel/resend) still succeeds.
    /// </summary>
    private async Task SubmitAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _smsGateway.SendSmsAsync(notification.ToPhoneNumber, notification.Body!, cancellationToken);
            notification.RecordSubmission(result.MessageSid, result.Status);
            _logger.LogInformation("Sent notification {NotificationId} for order {OrderId} (message {Sid}, status {Status}).",
                notification.Id, notification.OrderId, result.MessageSid, result.Status);
        }
        catch (Exception ex)
        {
            notification.RecordSubmissionFailure();
            _logger.LogWarning("Could not send notification {NotificationId} for order {OrderId}: {Error}.",
                notification.Id, notification.OrderId, ex.Message);
        }

        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    /// <summary>Refreshes the provider's delivery outcome for notifications that are not yet in a final state.</summary>
    private async Task RefreshDeliveryStateAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
                continue;
            if (FinalStatuses.Contains(notification.ProviderStatus))
                continue;

            try
            {
                var state = await _smsGateway.GetMessageStateAsync(notification.ProviderMessageSid, cancellationToken);
                notification.UpdateDeliveryState(state.Status, state.ErrorCode);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                // Reporting must not fail because the provider was briefly unreachable; keep last-known state.
                _logger.LogWarning("Could not refresh delivery state for notification {NotificationId}: {Error}.",
                    notification.Id, ex.Message);
            }
        }
    }
}
