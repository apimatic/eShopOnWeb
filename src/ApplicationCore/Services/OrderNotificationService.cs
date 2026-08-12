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

/// <summary>
/// Places orders and orchestrates the SMS messages that go out as an order moves. Every provider
/// interaction is best-effort: a message that cannot be sent is recorded but never fails the
/// underlying order operation.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    // "A few days later" for the post-dispatch delivery follow-up. Comfortably inside the provider's
    // 15-minute-to-35-day scheduling window.
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ISmsProvider _smsProvider;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        ISmsProvider smsProvider,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _smsProvider = smsProvider;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one line.", nameof(lines));
        }

        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new ArgumentException("Every order line must have a quantity of at least one.", nameof(lines));
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            throw new ArgumentException($"Unknown catalog item id(s): {string.Join(", ", missing)}.", nameof(lines));
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        // Reuse the app's existing order model. No ship-to address is collected on this API, so a
        // placeholder stands in for the required value object.
        var shipToAddress = new Address("N/A", "N/A", "N/A", "N/A", "00000");
        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        await NotifyAsync(order, NotificationType.OrderPlaced, BuildBody(NotificationType.OrderPlaced, order.Id), cancellationToken);

        return order;
    }

    public async Task<Order?> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        // Persist the state change first so a messaging problem can never undo the dispatch.
        order.MarkDispatched();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        await NotifyAsync(order, NotificationType.OrderDispatched, BuildBody(NotificationType.OrderDispatched, order.Id), cancellationToken);
        await ScheduleFollowUpAsync(order, cancellationToken);

        return order;
    }

    public async Task<Order?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        // Call off any follow-up that has not yet gone out — asking how delivery went for a cancelled
        // order is exactly the incident to prevent.
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        await NotifyAsync(order, NotificationType.OrderCancelled, BuildBody(NotificationType.OrderCancelled, order.Id), cancellationToken);

        return order;
    }

    public async Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var orders = await _orderRepository.ListAsync(new CustomerOrdersSpecification(buyerId), cancellationToken);
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByBuyerSpecification(buyerId), cancellationToken);

        await RefreshDeliveryStatesAsync(notifications, cancellationToken);

        var byOrder = notifications.ToLookup(n => n.OrderId);
        return orders
            .Select(o => new OrderWithNotifications(o, byOrder[o.Id].OrderBy(n => n.Id).ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsAsync(int orderId, string? restrictToBuyerId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        // An order belongs to the shopper who placed it; a shopper may only see their own.
        if (restrictToBuyerId is not null && order.BuyerId != restrictToBuyerId)
        {
            return null;
        }

        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshDeliveryStatesAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return ResendResult.NotEligible("An idempotency key is required.");
        }

        // Repeating a request under the same key must not send a second message.
        var priorForKey = await _notificationRepository.ListAsync(new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (priorForKey.Count > 0)
        {
            return ResendResult.Duplicate(priorForKey[0]);
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            return ResendResult.NotFound();
        }

        // Refresh the original's outcome so we don't re-send something that has since been delivered.
        await RefreshDeliveryStatesAsync(new[] { original }, cancellationToken);

        if (string.IsNullOrEmpty(original.ToPhoneNumber))
        {
            return ResendResult.NotEligible("The original notification has no destination number to re-send to.");
        }

        if (!NotificationStatus.IsUndeliverable(original.Status))
        {
            return ResendResult.NotEligible($"Only a message that did not reach the shopper can be re-sent (current status: '{original.Status}').");
        }

        var body = original.Body ?? BuildBody(original.Type, original.OrderId);
        var resend = OrderNotification.Create(original.OrderId, original.BuyerId, NotificationType.Resend, original.ToPhoneNumber, body);
        resend.MarkAsResendOf(original.Id, idempotencyKey);

        // Persist the key-bearing record before the send is attempted, so a duplicate request that
        // races this one finds it rather than sending again.
        resend = await _notificationRepository.AddAsync(resend, cancellationToken);
        await TrySendAsync(resend, sendAt: null, cancellationToken);
        await _notificationRepository.UpdateAsync(resend, cancellationToken);

        return ResendResult.Sent(resend);
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return false;
        }

        // Remove the text at the provider first so it is genuinely no longer retrievable there, then
        // locally. The fact that a message was sent, and what became of it, is preserved.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            await _smsProvider.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
        }

        notification.DisposeContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation($"Disposed of the content of notification {notificationId}.");
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _smsProvider.ListSentMessagesAsync(from, to, cancellationToken);
        var eshopNotifications = await _notificationRepository.ListAsync(new SentOrderNotificationsBetweenSpecification(from, to), cancellationToken);

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .ToDictionary(m => m.Sid, m => m);
        var eshopBySid = eshopNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationMatch>();
        var providerOnly = new List<ReconciliationEntry>();
        var eshopOnly = new List<ReconciliationEntry>();

        foreach (var (sid, message) in providerBySid)
        {
            if (eshopBySid.TryGetValue(sid, out var notification))
            {
                matched.Add(new ReconciliationMatch(sid, message.Status, notification.Status, notification.Id));
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry(sid, message.Status, null, message.DateSent));
            }
        }

        foreach (var (sid, notification) in eshopBySid)
        {
            if (!providerBySid.ContainsKey(sid))
            {
                eshopOnly.Add(new ReconciliationEntry(sid, notification.Status, notification.Id, notification.CreatedAt));
            }
        }

        return new ReconciliationReport(from, to, _smsProvider.FromNumber, matched, providerOnly, eshopOnly);
    }

    // ----- helpers ---------------------------------------------------------------------------

    /// <summary>
    /// Send an immediate message to each of the buyer's numbers, recording one notification per
    /// number. A buyer with no number on file is recorded as not-messaged.
    /// </summary>
    private async Task NotifyAsync(Order order, NotificationType type, string body, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);

        if (numbers.Count == 0)
        {
            var notMessaged = OrderNotification.CreateNotSent(order.Id, order.BuyerId, type, body);
            await _notificationRepository.AddAsync(notMessaged, cancellationToken);
            return;
        }

        foreach (var number in numbers)
        {
            var notification = OrderNotification.Create(order.Id, order.BuyerId, type, number.PhoneNumber, body);
            notification = await _notificationRepository.AddAsync(notification, cancellationToken);
            await TrySendAsync(notification, sendAt: null, cancellationToken);
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
    }

    /// <summary>
    /// Queue a "how did delivery go" follow-up with the provider for a few days after dispatch — one
    /// per registered number. The provider holds the schedule; this application runs no timer.
    /// </summary>
    private async Task ScheduleFollowUpAsync(Order order, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var body = BuildBody(NotificationType.DeliveryFollowUp, order.Id);

        foreach (var number in numbers)
        {
            var notification = OrderNotification.Create(order.Id, order.BuyerId, NotificationType.DeliveryFollowUp, number.PhoneNumber, body);
            notification.MarkAsFollowUp(sendAt);
            notification = await _notificationRepository.AddAsync(notification, cancellationToken);
            await TrySendAsync(notification, sendAt, cancellationToken);
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in notifications.Where(n => n.IsFollowUp && !NotificationStatus.IsTerminal(n.Status)))
        {
            if (string.IsNullOrEmpty(followUp.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                await _smsProvider.CancelScheduledAsync(followUp.ProviderMessageSid, cancellationToken);
                followUp.MarkCanceled();
                await _notificationRepository.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to cancel scheduled follow-up notification {followUp.Id}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Attempt a send, folding the result into the notification. Never throws: a messaging failure
    /// must not fail the operation that triggered it.
    /// </summary>
    private async Task TrySendAsync(OrderNotification notification, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(notification.ToPhoneNumber))
        {
            return;
        }

        try
        {
            var request = new SendSmsRequest(notification.ToPhoneNumber, notification.Body ?? string.Empty) { SendAt = sendAt };
            var result = await _smsProvider.SendAsync(request, cancellationToken);
            notification.RecordAccepted(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage);
        }
        catch (Exception ex)
        {
            // Deliberately do not include the destination number in the log line.
            _logger.LogWarning($"Failed to send notification {notification.Id} (type {notification.Type}): {ex.Message}");
            notification.RecordSendFailed(null, ex.Message);
        }
    }

    /// <summary>
    /// Refresh delivery outcomes from the provider for messages that are not yet in a terminal state.
    /// </summary>
    private async Task RefreshDeliveryStatesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid) || NotificationStatus.IsTerminal(notification.Status))
            {
                continue;
            }

            try
            {
                var message = await _smsProvider.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                if (message is not null)
                {
                    notification.UpdateDeliveryState(message.Status, message.ErrorCode, message.ErrorMessage);
                    await _notificationRepository.UpdateAsync(notification, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to refresh delivery state for notification {notification.Id}: {ex.Message}");
            }
        }
    }

    private static string BuildBody(NotificationType type, int orderId) => type switch
    {
        NotificationType.OrderPlaced => $"eShopOnWeb: Thanks! Your order #{orderId} has been placed.",
        NotificationType.OrderDispatched => $"eShopOnWeb: Good news - your order #{orderId} is on its way!",
        NotificationType.DeliveryFollowUp => $"eShopOnWeb: How did the delivery of order #{orderId} go? Reply to let us know.",
        NotificationType.OrderCancelled => $"eShopOnWeb: Your order #{orderId} has been cancelled. Contact support if this is unexpected.",
        _ => $"eShopOnWeb: An update about your order #{orderId}."
    };
}
