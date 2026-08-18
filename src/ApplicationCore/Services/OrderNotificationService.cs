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
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How far out the "how did delivery go?" follow-up is queued with the provider.</summary>
    private const int FollowUpDelayDays = 3;

    private static readonly HashSet<string> TerminalStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "delivered", "undelivered", "failed", "canceled" };

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IReadRepository<ContactNumber> _contactNumberRepository;
    private readonly IReadRepository<CatalogItem> _itemRepository;
    private readonly ISmsProvider _smsProvider;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IRepository<OrderNotification> notificationRepository,
        IReadRepository<ContactNumber> contactNumberRepository,
        IReadRepository<CatalogItem> itemRepository,
        ISmsProvider smsProvider,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _itemRepository = itemRepository;
        _smsProvider = smsProvider;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines is null || lines.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one line.", nameof(lines));
        }

        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new ArgumentException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.", nameof(lines));
            }
        }

        var itemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(itemIds), cancellationToken);
        if (catalogItems.Count != itemIds.Length)
        {
            var missing = itemIds.Except(catalogItems.Select(c => c.Id));
            throw new ArgumentException($"Unknown catalog item(s): {string.Join(", ", missing)}.", nameof(lines));
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var pictureUri = string.IsNullOrEmpty(catalogItem.PictureUri) ? "eCatalog-item-default.png" : catalogItem.PictureUri;
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(pictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);
        _logger.LogInformation("Placed order {0} for buyer {1}.", order.Id, buyerId);

        // Tell the shopper their order was placed. A messaging failure must not fail the order.
        await NotifyImmediateAsync(order, NotificationKind.OrderPlaced, cancellationToken);

        return order;
    }

    public async Task<IReadOnlyList<OrderNotification>?> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        var produced = new List<OrderNotification>();

        // Tell the shopper it is on its way.
        produced.AddRange(await NotifyImmediateAsync(order, NotificationKind.Dispatched, cancellationToken));

        // Queue a follow-up asking how delivery went, with the provider, a few days later.
        var sendAt = DateTimeOffset.UtcNow.AddDays(FollowUpDelayDays);
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
        var body = ComposeBody(NotificationKind.DeliveryFollowUp, order.Id);

        foreach (var number in numbers)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, NotificationKind.DeliveryFollowUp, number.PhoneNumber, body, scheduledSendAt: sendAt);
            try
            {
                var result = await _smsProvider.ScheduleAsync(number.PhoneNumber, body, sendAt, cancellationToken);
                notification.RecordProviderResult(result.MessageSid, result.Status, result.ErrorCode, result.ErrorMessage);
            }
            catch (SmsProviderException ex)
            {
                notification.RecordSendFailure(ex.Message);
                _logger.LogWarning("Could not queue delivery follow-up for order {0}: {1}", order.Id, ex.Message);
            }

            await _notificationRepository.AddAsync(notification, cancellationToken);
            produced.Add(notification);
        }

        _logger.LogInformation("Dispatched order {0}; raised {1} notification(s).", order.Id, produced.Count);
        return produced;
    }

    public async Task<IReadOnlyList<OrderNotification>?> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        var affected = new List<OrderNotification>();

        // Tell the shopper it was cancelled.
        affected.AddRange(await NotifyImmediateAsync(order, NotificationKind.Cancelled, cancellationToken));

        // Call off any follow-up that has not yet gone out — a "how did delivery go?" text for a cancelled
        // order is exactly the incident this prevents.
        var existing = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(order.Id), cancellationToken);
        foreach (var followUp in existing.Where(IsCancellableFollowUp))
        {
            try
            {
                var result = await _smsProvider.CancelScheduledAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.UpdateDeliveryState(result.Status, result.ErrorCode, result.ErrorMessage);
                _logger.LogInformation("Called off scheduled follow-up {0} for cancelled order {1}.", followUp.Id, order.Id);
            }
            catch (SmsProviderException ex)
            {
                // Cancelling the order must still succeed even if calling off the follow-up failed.
                _logger.LogWarning("Could not call off follow-up {0} for order {1}: {2}", followUp.Id, order.Id, ex.Message);
            }

            await _notificationRepository.UpdateAsync(followUp, cancellationToken);
            affected.Add(followUp);
        }

        _logger.LogInformation("Cancelled order {0}.", order.Id);
        return affected;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> GetNotificationsForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByBuyerSpecification(buyerId), cancellationToken);
        await RefreshDeliveryStateAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>?> GetNotificationsForOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            // Another shopper's order (or none) — indistinguishable from "not found" to this caller.
            return null;
        }

        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshDeliveryStateAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<OrderNotification?> GetNotificationAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        return await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
    }

    public async Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        // Idempotency: a repeat under the same key returns the notification the first request produced,
        // without sending a second message.
        var existing = await _notificationRepository.FirstOrDefaultAsync(new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("Resend under existing idempotency key returned notification {0} without re-sending.", existing.Id);
            return existing;
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            return null;
        }

        var body = original.MessageBody ?? ComposeBody(original.Kind, original.OrderId);
        var resend = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            original.Kind,
            original.ToPhoneNumber,
            body,
            idempotencyKey: idempotencyKey,
            resendOfNotificationId: original.Id);

        try
        {
            var result = await _smsProvider.SendAsync(original.ToPhoneNumber, body, cancellationToken);
            resend.RecordProviderResult(result.MessageSid, result.Status, result.ErrorCode, result.ErrorMessage);
        }
        catch (SmsProviderException ex)
        {
            resend.RecordSendFailure(ex.Message);
            _logger.LogWarning("Resend of notification {0} could not be handed to the provider: {1}", original.Id, ex.Message);
        }

        await _notificationRepository.AddAsync(resend, cancellationToken);
        _logger.LogInformation("Resent notification {0} as {1}.", original.Id, resend.Id);
        return resend;
    }

    public async Task<bool?> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            // Dispose of the content at the provider too — a failure here surfaces to the caller
            // (we must not claim the text is gone when it is not). The record itself survives.
            await _smsProvider.RedactContentAsync(notification.ProviderMessageSid!, cancellationToken);
        }

        notification.MarkContentDisposed();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Disposed of content for notification {0}.", notificationId);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _smsProvider.ListSentMessagesAsync(from, to, cancellationToken);

        var allNotifications = await _notificationRepository.ListAsync(cancellationToken);

        // eShop's own view for this range: messages we believe actually went to the provider to be sent
        // (a scheduled-but-not-yet-sent or cancelled follow-up is deliberately excluded — the provider's
        // send-time listing would not include it either).
        var eShopSent = allNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .Where(n => n.ProviderStatus is null || (!string.Equals(n.ProviderStatus, "scheduled", StringComparison.OrdinalIgnoreCase)
                                                     && !string.Equals(n.ProviderStatus, "canceled", StringComparison.OrdinalIgnoreCase)))
            .Where(n => n.CreatedDate >= from && n.CreatedDate <= to)
            .ToList();

        var eShopBySid = eShopSent
            .Where(n => n.ProviderMessageSid is not null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid!)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var eShopOnly = new List<ReconciliationEntry>();

        foreach (var pm in providerBySid)
        {
            if (eShopBySid.TryGetValue(pm.Key, out var ours))
            {
                matched.Add(new ReconciliationEntry
                {
                    MessageSid = pm.Key,
                    ProviderStatus = pm.Value.Status,
                    EShopStatus = ours.ProviderStatus,
                    OrderId = ours.OrderId,
                    DateSent = pm.Value.DateSent
                });
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry
                {
                    MessageSid = pm.Key,
                    ProviderStatus = pm.Value.Status,
                    DateSent = pm.Value.DateSent
                });
            }
        }

        foreach (var ours in eShopBySid)
        {
            if (!providerBySid.ContainsKey(ours.Key))
            {
                eShopOnly.Add(new ReconciliationEntry
                {
                    MessageSid = ours.Key,
                    EShopStatus = ours.Value.ProviderStatus,
                    OrderId = ours.Value.OrderId
                });
            }
        }

        _logger.LogInformation("Reconciliation [{0:o}..{1:o}]: {2} matched, {3} provider-only, {4} eShop-only.",
            from, to, matched.Count, providerOnly.Count, eShopOnly.Count);

        return new ReconciliationReport
        {
            From = from,
            To = to,
            Matched = matched,
            ProviderOnly = providerOnly,
            EShopOnly = eShopOnly
        };
    }

    /// <summary>Send a message now to each of the buyer's registered numbers and record each attempt.</summary>
    private async Task<List<OrderNotification>> NotifyImmediateAsync(Order order, NotificationKind kind, CancellationToken cancellationToken)
    {
        var produced = new List<OrderNotification>();
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);

        // A shopper with no number on file is simply not messaged.
        if (numbers.Count == 0)
        {
            _logger.LogInformation("Order {0} {1}: buyer has no number on file; not messaged.", order.Id, kind);
            return produced;
        }

        var body = ComposeBody(kind, order.Id);
        foreach (var number in numbers)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, kind, number.PhoneNumber, body);
            try
            {
                var result = await _smsProvider.SendAsync(number.PhoneNumber, body, cancellationToken);
                notification.RecordProviderResult(result.MessageSid, result.Status, result.ErrorCode, result.ErrorMessage);
            }
            catch (SmsProviderException ex)
            {
                notification.RecordSendFailure(ex.Message);
                _logger.LogWarning("Order {0} {1}: message could not be handed to the provider: {2}", order.Id, kind, ex.Message);
            }

            await _notificationRepository.AddAsync(notification, cancellationToken);
            produced.Add(notification);
        }

        return produced;
    }

    /// <summary>Refresh the delivery outcome of each non-terminal, provider-known notification.</summary>
    private async Task RefreshDeliveryStateAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            if (notification.ProviderStatus is not null && TerminalStatuses.Contains(notification.ProviderStatus))
            {
                continue;
            }

            try
            {
                var result = await _smsProvider.GetStatusAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.UpdateDeliveryState(result.Status, result.ErrorCode, result.ErrorMessage);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (SmsProviderException ex)
            {
                // A provider hiccup on a read must not fail the caller's request; keep the last-known state.
                _logger.LogWarning("Could not refresh delivery state for notification {0}: {1}", notification.Id, ex.Message);
            }
        }
    }

    private static bool IsCancellableFollowUp(OrderNotification n) =>
        n.Kind == NotificationKind.DeliveryFollowUp
        && !string.IsNullOrEmpty(n.ProviderMessageSid)
        && string.Equals(n.ProviderStatus, "scheduled", StringComparison.OrdinalIgnoreCase);

    private static string ComposeBody(NotificationKind kind, int orderId) => kind switch
    {
        NotificationKind.OrderPlaced => $"eShopOnWeb: Your order #{orderId} has been placed. Thank you for shopping with us!",
        NotificationKind.Dispatched => $"eShopOnWeb: Good news! Your order #{orderId} is on its way.",
        NotificationKind.DeliveryFollowUp => $"eShopOnWeb: How did the delivery of your order #{orderId} go? We'd love your feedback.",
        NotificationKind.Cancelled => $"eShopOnWeb: Your order #{orderId} has been cancelled. If this is unexpected, please contact support.",
        _ => $"eShopOnWeb: An update on your order #{orderId}."
    };
}
