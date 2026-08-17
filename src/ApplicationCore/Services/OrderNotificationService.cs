using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
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
    /// <summary>How far ahead the delivery follow-up is queued with the provider.</summary>
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IMessagingProvider _messagingProvider;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogRepository,
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        IMessagingProvider messagingProvider,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _catalogRepository = catalogRepository;
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _messagingProvider = messagingProvider;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address shipToAddress, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines is null || lines.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one item.", nameof(lines));
        }

        var catalogItemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new ArgumentException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.", nameof(lines));
            }

            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId);
            if (catalogItem is null)
            {
                throw new ArgumentException($"Catalog item {line.CatalogItemId} was not found.", nameof(lines));
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);

        await NotifyAsync(order, NotificationType.OrderPlaced, OrderPlacedBody(order.Id), cancellationToken);

        return order;
    }

    public async Task<bool> DispatchOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return false;
        }

        // Tell the shopper it is on its way...
        await NotifyAsync(order, NotificationType.OrderDispatched, DispatchedBody(order.Id), cancellationToken);

        // ...and queue the "how did delivery go?" follow-up with the provider for a few days later.
        await ScheduleFollowUpAsync(order, cancellationToken);

        return true;
    }

    public async Task<bool> CancelOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return false;
        }

        // Call off any follow-up still queued at the provider FIRST — a customer must never be asked how a
        // cancelled order's delivery went.
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        // Then tell the shopper it was cancelled.
        await NotifyAsync(order, NotificationType.OrderCancelled, CancelledBody(order.Id), cancellationToken);

        return true;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> GetNotificationsForBuyerAsync(string buyerId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByBuyerSpecification(buyerId), cancellationToken);
        await RefreshDeliveryOutcomesAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>?> GetNotificationsForOrderAsync(int orderId, string? buyerIdScope, CancellationToken cancellationToken)
    {
        IReadOnlyList<OrderNotification> notifications;
        if (buyerIdScope is not null)
        {
            // Shopper scope: the order (and thus its notifications) must belong to the caller.
            var order = await _orderRepository.FirstOrDefaultAsync(new CustomerOrderByIdSpecification(orderId, buyerIdScope), cancellationToken);
            if (order is null)
            {
                return null;
            }
            notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId, buyerIdScope), cancellationToken);
        }
        else
        {
            notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        }

        await RefreshDeliveryOutcomesAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            return ResendResult.NotFound();
        }

        // Idempotency: a repeat under the same key returns the message the first request produced.
        var alreadySent = await _notificationRepository.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(notificationId, idempotencyKey), cancellationToken);
        if (alreadySent is not null)
        {
            return ResendResult.ReusedExisting(alreadySent.Id);
        }

        if (string.IsNullOrEmpty(original.MessageBody))
        {
            return ResendResult.Failed("The message content has been disposed of and cannot be re-sent.");
        }

        var resend = OrderNotification.ResendOf(original, idempotencyKey);
        await _notificationRepository.AddAsync(resend, cancellationToken);

        await TrySendAsync(resend, cancellationToken);
        await _notificationRepository.UpdateAsync(resend, cancellationToken);

        return ResendResult.Sent(resend.Id);
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return false;
        }

        // Dispose of the content at the provider first, so we only claim disposal once the provider confirms
        // it. A provider failure propagates (mapped at the boundary) rather than leaving a false confirmation.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            await _messagingProvider.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);

        _logger.LogInformation("Disposed of content for notification {NotificationId} (order {OrderId}).", notification.Id, notification.OrderId);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken)
    {
        var fromNumber = _messagingProvider.SendingNumber;

        // Ask the provider only for messages from our own configured sending number, over the whole range.
        var providerMessages = await _messagingProvider.ListMessagesAsync(fromNumber, fromUtc, toUtc, cancellationToken);

        // What eShop believes it sent: notifications that reached the provider (carry a SID).
        var allNotifications = await _notificationRepository.ListAsync(cancellationToken);
        var eShopBySid = allNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var providerSids = new HashSet<string>(providerMessages.Where(m => !string.IsNullOrEmpty(m.Sid)).Select(m => m.Sid!));

        var entries = new List<ReconciliationEntry>();

        // Provider's view: every message the provider has for our number in range, matched to eShop where possible.
        foreach (var message in providerMessages)
        {
            eShopBySid.TryGetValue(message.Sid ?? string.Empty, out var match);
            entries.Add(new ReconciliationEntry(
                match is not null ? ReconciliationMatch.InBoth : ReconciliationMatch.ProviderOnly,
                message.Sid,
                message.Status,
                message.DateSent,
                match?.Id,
                match?.OrderId,
                match?.DeliveryStatus));
        }

        // eShop-only: notifications eShop recorded as sent (in range) that the provider's answer does not include.
        foreach (var notification in allNotifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid)) continue;
            if (providerSids.Contains(notification.ProviderMessageSid)) continue;
            if (notification.CreatedAt < fromUtc || notification.CreatedAt > toUtc) continue;

            entries.Add(new ReconciliationEntry(
                ReconciliationMatch.EShopOnly,
                notification.ProviderMessageSid,
                null,
                null,
                notification.Id,
                notification.OrderId,
                notification.DeliveryStatus));
        }

        var inBoth = entries.Count(e => e.Match == ReconciliationMatch.InBoth);
        var providerOnly = entries.Count(e => e.Match == ReconciliationMatch.ProviderOnly);
        var eShopOnly = entries.Count(e => e.Match == ReconciliationMatch.EShopOnly);

        return new ReconciliationReport(
            fromUtc,
            toUtc,
            fromNumber,
            providerMessages.Count,
            inBoth + eShopOnly,
            inBoth,
            providerOnly,
            eShopOnly,
            entries);
    }

    // ----- helpers -----

    private async Task NotifyAsync(Order order, NotificationType type, string body, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
        if (numbers.Count == 0)
        {
            // A shopper with no number on file is simply not messaged.
            _logger.LogInformation("Order {OrderId}: no contact number on file for a {Type} notification; nothing sent.", order.Id, type);
            return;
        }

        foreach (var number in numbers)
        {
            var notification = OrderNotification.Immediate(order.Id, order.BuyerId, type, number.E164Number, body);
            await _notificationRepository.AddAsync(notification, cancellationToken);

            await TrySendAsync(notification, cancellationToken);
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
    }

    private async Task ScheduleFollowUpAsync(Order order, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
        if (numbers.Count == 0)
        {
            return;
        }

        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        foreach (var number in numbers)
        {
            var notification = OrderNotification.Scheduled(order.Id, order.BuyerId, NotificationType.DeliveryFollowUp, number.E164Number, FollowUpBody(order.Id), sendAt);
            await _notificationRepository.AddAsync(notification, cancellationToken);

            try
            {
                var sent = await _messagingProvider.ScheduleSmsAsync(number.E164Number, FollowUpBody(order.Id), sendAt, cancellationToken);
                notification.MarkSent(sent.ProviderMessageSid, sent.Status);
                _logger.LogInformation("Order {OrderId}: follow-up notification {NotificationId} scheduled ({Sid}).", order.Id, notification.Id, sent.ProviderMessageSid);
            }
            catch (MessagingProviderException ex)
            {
                notification.MarkSendFailed(ex.ProviderErrorCode, ex.ProviderErrorMessage ?? ex.Message);
                _logger.LogWarning("Order {OrderId}: follow-up notification {NotificationId} could not be scheduled (provider status {Status}).", order.Id, notification.Id, ex.StatusCode);
            }

            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notificationRepository.ListAsync(new PendingScheduledFollowUpsByOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            if (string.IsNullOrEmpty(followUp.ProviderMessageSid)) continue;

            try
            {
                var status = await _messagingProvider.CancelScheduledAsync(followUp.ProviderMessageSid, cancellationToken);
                followUp.UpdateDeliveryStatus(status.Status, status.ErrorCode, status.ErrorMessage);
                _logger.LogInformation("Order {OrderId}: follow-up notification {NotificationId} called off (status {Status}).", orderId, followUp.Id, status.Status);
            }
            catch (MessagingProviderException ex)
            {
                // Already sent / not cancelable is a benign outcome, not a failure of the cancel operation.
                _logger.LogWarning("Order {OrderId}: follow-up notification {NotificationId} could not be called off (provider status {Status}); leaving as-is.", orderId, followUp.Id, ex.StatusCode);
            }

            await _notificationRepository.UpdateAsync(followUp, cancellationToken);
        }
    }

    /// <summary>Sends an immediate notification best-effort; a send failure is recorded, never thrown.</summary>
    private async Task TrySendAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var sent = await _messagingProvider.SendSmsAsync(notification.ToNumber, notification.MessageBody ?? string.Empty, cancellationToken);
            notification.MarkSent(sent.ProviderMessageSid, sent.Status);
            _logger.LogInformation("Order {OrderId}: notification {NotificationId} ({Type}) sent ({Sid}, {Status}).",
                notification.OrderId, notification.Id, notification.Type, sent.ProviderMessageSid, sent.Status);
        }
        catch (MessagingProviderException ex)
        {
            notification.MarkSendFailed(ex.ProviderErrorCode, ex.ProviderErrorMessage ?? ex.Message);
            _logger.LogWarning("Order {OrderId}: notification {NotificationId} ({Type}) could not be sent (provider status {Status}).",
                notification.OrderId, notification.Id, notification.Type, ex.StatusCode);
        }
    }

    /// <summary>Best-effort refresh of non-terminal notifications' delivery outcomes from the provider.</summary>
    private async Task RefreshDeliveryOutcomesAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid)) continue;
            if (NotificationDeliveryState.IsTerminal(notification.DeliveryStatus)) continue;

            try
            {
                var status = await _messagingProvider.GetStatusAsync(notification.ProviderMessageSid, cancellationToken);
                notification.UpdateDeliveryStatus(status.Status, status.ErrorCode, status.ErrorMessage);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (MessagingProviderException ex)
            {
                _logger.LogWarning("Notification {NotificationId}: could not refresh delivery outcome (provider status {Status}).", notification.Id, ex.StatusCode);
            }
        }
    }

    private static string OrderPlacedBody(int orderId) => $"eShopOnWeb: your order #{orderId} has been placed. Thank you for shopping with us!";
    private static string DispatchedBody(int orderId) => $"eShopOnWeb: good news - your order #{orderId} is on its way!";
    private static string FollowUpBody(int orderId) => $"eShopOnWeb: how did the delivery of your order #{orderId} go? We'd love your feedback.";
    private static string CancelledBody(int orderId) => $"eShopOnWeb: your order #{orderId} has been cancelled. If this was unexpected, please contact support.";
}
