using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NotFoundException = Microsoft.eShopWeb.ApplicationCore.Exceptions.NotFoundException;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderMessagingService : IOrderMessagingService
{
    // Delivery states we won't keep re-polling the provider for — the outcome is settled.
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        NotificationStatus.Delivered,
        NotificationStatus.Undelivered,
        NotificationStatus.Failed,
        NotificationStatus.Canceled
    };

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly INotificationGateway _gateway;
    private readonly INotificationSettings _settings;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderMessagingService> _logger;

    public OrderMessagingService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        INotificationGateway gateway,
        INotificationSettings settings,
        IUriComposer uriComposer,
        IAppLogger<OrderMessagingService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _gateway = gateway;
        _settings = settings;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines is null || lines.Count == 0)
        {
            throw new BadRequestException("An order must contain at least one item.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new BadRequestException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId);
            if (catalogItem is null)
            {
                throw new NotFoundException($"Catalog item {line.CatalogItemId} was not found.");
            }
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, orderItems);
        await _orders.AddAsync(order, cancellationToken);

        await SafeNotifyAsync(order, NotificationType.OrderPlaced, OrderNotificationMessages.Placed(order), cancellationToken);
        return order;
    }

    public async Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken)
            ?? throw new NotFoundException($"Order {orderId} was not found.");

        order.Dispatch();
        await _orders.UpdateAsync(order, cancellationToken);

        await SafeNotifyAsync(order, NotificationType.OrderDispatched, OrderNotificationMessages.Dispatched(order), cancellationToken);
        await SafeScheduleFollowUpAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken)
            ?? throw new NotFoundException($"Order {orderId} was not found.");

        order.Cancel();
        await _orders.UpdateAsync(order, cancellationToken);

        // Call off any follow-up that hasn't gone out BEFORE telling the shopper — a "how did delivery
        // go?" text for a cancelled order is exactly the incident this must prevent.
        await SafeCancelPendingFollowUpsAsync(order.Id, cancellationToken);
        await SafeNotifyAsync(order, NotificationType.OrderCancelled, OrderNotificationMessages.Cancelled(order), cancellationToken);
        return order;
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));
        return await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    public async Task<Order> GetOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            throw new NotFoundException($"Order {orderId} was not found.");
        }
        return order;
    }

    public async Task<IReadOnlyList<OrderNotification>> GetNotificationsForOrdersAsync(IReadOnlyCollection<int> orderIds, CancellationToken cancellationToken = default)
    {
        if (orderIds is null || orderIds.Count == 0)
        {
            return Array.Empty<OrderNotification>();
        }
        var notifications = await _notifications.ListAsync(new NotificationsByOrderIdsSpecification(orderIds), cancellationToken);
        await RefreshDeliveryStateAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(idempotencyKey, nameof(idempotencyKey));

        // Idempotency: a repeat under the same key returns the message the first attempt produced,
        // without sending anything again.
        var alreadyDone = await _notifications.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (alreadyDone is not null)
        {
            _logger.LogInformation("Resend for key hit an existing notification {0}; not sending again.", alreadyDone.Id);
            return alreadyDone;
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotFoundException($"Notification {notificationId} was not found.");

        if (string.IsNullOrEmpty(original.Body))
        {
            throw new InvalidOrderStateException($"Notification {notificationId} has no content to resend (it was disposed of).");
        }

        var resend = new OrderNotification(original.OrderId, original.OwnerId, original.ToNumber, original.Type, original.Body);
        resend.MarkAsResendOf(original.Id, idempotencyKey);

        try
        {
            var result = await _gateway.SendAsync(original.ToNumber, original.Body, cancellationToken);
            resend.RecordSendResult(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage, result.SentAt);
        }
        catch (Exception ex)
        {
            resend.RecordSendError(Sanitize(ex));
            _logger.LogWarning("Resend of notification {0} failed to reach the provider: {1}", original.Id, Sanitize(ex));
        }

        // Persist regardless of send outcome so the idempotency key is durable.
        await _notifications.AddAsync(resend, cancellationToken);
        return resend;
    }

    public async Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotFoundException($"Notification {notificationId} was not found.");

        // Redact at the provider first so the text is genuinely no longer retrievable there. If this
        // fails the disposal fails — we do not claim to have disposed of content we could not.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            await _gateway.RedactContentAsync(notification.ProviderMessageSid, cancellationToken);
        }

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Disposed of content for notification {0}; record and outcome retained.", notification.Id);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        // The provider is asked only for messages sent from our configured number, over the range.
        var providerMessages = await _gateway.ListSentFromConfiguredNumberAsync(from, to, cancellationToken);

        var allSent = await _notifications.ListAsync(new SentNotificationsSpecification(), cancellationToken);
        var eShopSent = allSent
            // "eShop believes it sent" = handed to the provider for immediate delivery and not called off.
            // A still-scheduled, cancelled-before-send, or never-reached message was not sent.
            .Where(n => n.ProviderMessageSid is not null
                && !n.IsScheduled
                && n.Status != NotificationStatus.Scheduled
                && n.Status != NotificationStatus.Canceled
                && n.Status != NotificationStatus.SendError)
            .Where(n =>
            {
                var t = n.SentAt ?? n.CreatedDate;
                return t >= from && t <= to;
            })
            .ToList();

        var eShopBySid = eShopSent
            .Where(n => n.ProviderMessageSid is not null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var providerBySid = providerMessages
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var matched = new List<ReconciliationEntry>();
        var inProviderOnly = new List<ReconciliationEntry>();
        var inEShopOnly = new List<ReconciliationEntry>();

        foreach (var pm in providerMessages)
        {
            if (eShopBySid.TryGetValue(pm.Sid, out var n))
            {
                matched.Add(new ReconciliationEntry(pm.Sid, pm.Status, pm.DateSent, n.Id, n.OrderId, n.Status));
            }
            else
            {
                inProviderOnly.Add(new ReconciliationEntry(pm.Sid, pm.Status, pm.DateSent, null, null, null));
            }
        }

        foreach (var n in eShopSent)
        {
            if (!providerBySid.ContainsKey(n.ProviderMessageSid!))
            {
                inEShopOnly.Add(new ReconciliationEntry(n.ProviderMessageSid!, null, n.SentAt, n.Id, n.OrderId, n.Status));
            }
        }

        return new ReconciliationReport(
            from, to,
            providerMessages.Count,
            eShopSent.Count,
            matched.Count,
            matched,
            inProviderOnly,
            inEShopOnly);
    }

    // ---- internal helpers -------------------------------------------------

    /// <summary>
    /// Sends a message to every number the order's buyer has on file, recording an
    /// <see cref="OrderNotification"/> for each. Never throws: a messaging failure must not fail the
    /// order operation. A buyer with no number on file is simply not messaged.
    /// </summary>
    private async Task SafeNotifyAsync(Order order, NotificationType type, string body, CancellationToken cancellationToken)
    {
        try
        {
            var numbers = await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(order.BuyerId), cancellationToken);
            foreach (var number in numbers)
            {
                await SendOneAsync(order, number, type, body, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Notifying order {0} ({1}) failed but the order operation stands: {2}", order.Id, type, Sanitize(ex));
        }
    }

    private async Task SendOneAsync(Order order, ContactNumber number, NotificationType type, string body, CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(order.Id, order.BuyerId, number.Number, type, body);
        try
        {
            var result = await _gateway.SendAsync(number.Number, body, cancellationToken);
            notification.RecordSendResult(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage, result.SentAt);
        }
        catch (Exception ex)
        {
            notification.RecordSendError(Sanitize(ex));
            _logger.LogWarning("Send for order {0} ({1}) failed: {2}", order.Id, type, Sanitize(ex));
        }
        await _notifications.AddAsync(notification, cancellationToken);
    }

    private async Task SafeScheduleFollowUpAsync(Order order, CancellationToken cancellationToken)
    {
        try
        {
            var body = OrderNotificationMessages.DeliveryFollowUp(order);
            var sendAt = DateTimeOffset.UtcNow.AddDays(Math.Max(1, _settings.FollowUpDelayDays));
            var numbers = await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(order.BuyerId), cancellationToken);
            foreach (var number in numbers)
            {
                var notification = new OrderNotification(order.Id, order.BuyerId, number.Number, NotificationType.DeliveryFollowUp, body);
                try
                {
                    var result = await _gateway.ScheduleAsync(number.Number, body, sendAt, cancellationToken);
                    notification.RecordScheduled(result.Sid, result.Status, sendAt);
                }
                catch (Exception ex)
                {
                    notification.RecordSendError(Sanitize(ex));
                    _logger.LogWarning("Scheduling follow-up for order {0} failed: {1}", order.Id, Sanitize(ex));
                }
                await _notifications.AddAsync(notification, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Scheduling follow-up for order {0} failed but dispatch stands: {1}", order.Id, Sanitize(ex));
        }
    }

    private async Task SafeCancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        try
        {
            var pending = await _notifications.ListAsync(new PendingScheduledNotificationsByOrderSpecification(orderId), cancellationToken);
            foreach (var notification in pending)
            {
                try
                {
                    if (notification.ProviderMessageSid is not null)
                    {
                        await _gateway.CancelScheduledAsync(notification.ProviderMessageSid, cancellationToken);
                    }
                    notification.MarkCancelled();
                    await _notifications.UpdateAsync(notification, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Cancelling follow-up (notification {0}) for order {1} failed: {2}", notification.Id, orderId, Sanitize(ex));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Cancelling follow-ups for order {0} failed but cancellation stands: {1}", orderId, Sanitize(ex));
        }
    }

    private async Task RefreshDeliveryStateAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (notification.ProviderMessageSid is null || TerminalStatuses.Contains(notification.Status))
            {
                continue;
            }
            try
            {
                var state = await _gateway.FetchStateAsync(notification.ProviderMessageSid, cancellationToken);
                if (state is not null)
                {
                    notification.UpdateDeliveryState(state.Status, state.ErrorCode, state.ErrorMessage, state.SentAt);
                    await _notifications.UpdateAsync(notification, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Refreshing delivery state for notification {0} failed: {1}", notification.Id, Sanitize(ex));
            }
        }
    }

    /// <summary>Keeps exception text safe to log — provider errors do not include the destination number.</summary>
    private static string Sanitize(Exception ex) => ex.Message;
}
