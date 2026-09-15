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

/// <summary>
/// Orchestrates order placement/dispatch/cancellation and the SMS notifications that accompany
/// them. Sending is best-effort: a message that cannot go out is recorded against the order but
/// never fails the underlying operation.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How far ahead the "how was delivery?" follow-up is queued with the provider.</summary>
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogRepository;
    private readonly IRepository<ContactNumber> _contactRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ISmsNotificationService _sms;
    private readonly IResendIdempotencyGuard _idempotencyGuard;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogRepository,
        IRepository<ContactNumber> contactRepository,
        IRepository<OrderNotification> notificationRepository,
        ISmsNotificationService sms,
        IResendIdempotencyGuard idempotencyGuard,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _catalogRepository = catalogRepository;
        _contactRepository = contactRepository;
        _notificationRepository = notificationRepository;
        _sms = sms;
        _idempotencyGuard = idempotencyGuard;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines, Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(lines, nameof(lines));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines.Count == 0)
            throw new ArgumentException("An order must contain at least one line.", nameof(lines));

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
                throw new ArgumentException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.", nameof(lines));

            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new ArgumentException($"Catalog item {line.CatalogItemId} does not exist.", nameof(lines));

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, items);
        await _orderRepository.AddAsync(order, cancellationToken);

        await SendImmediateToShopperAsync(order, NotificationKind.OrderPlaced, cancellationToken);
        return order;
    }

    public async Task<Order?> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
            return null;

        await SendImmediateToShopperAsync(order, NotificationKind.OrderDispatched, cancellationToken);
        await ScheduleFollowUpToShopperAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
            return null;

        // Call off any not-yet-sent follow-up first, so it can never slip out for a cancelled order.
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);
        await SendImmediateToShopperAsync(order, NotificationKind.OrderCancelled, cancellationToken);
        return order;
    }

    public async Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        return await _idempotencyGuard.RunExclusivelyAsync<OrderNotification?>(idempotencyKey, async () =>
        {
            // A repeat under the same key returns the message the first attempt produced; it sends nothing.
            var existing = await _notificationRepository.FirstOrDefaultAsync(
                new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
            if (existing is not null)
                return existing;

            var source = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
            if (source is null)
                return null;

            var body = BodyForKind(source.Kind, source.OrderId);
            var resend = new OrderNotification(source.OrderId, source.OwnerId, source.Kind, source.ToNumber, NotificationStatus.SendFailed);
            try
            {
                var sent = await _sms.SendAsync(source.ToNumber, body, cancellationToken);
                resend.MarkSent(sent.ProviderMessageSid, sent.Status);
            }
            catch (SmsNotificationException ex)
            {
                resend.MarkSendFailed(NotificationStatus.SendFailed, ex.Message);
                _logger.LogWarning("Resend for notification {NotificationId} (order {OrderId}) could not be sent: {Reason}",
                    notificationId, source.OrderId, ex.Message);
            }

            resend.SetIdempotencyKey(idempotencyKey);
            await _notificationRepository.AddAsync(resend, cancellationToken);
            return resend;
        });
    }

    public async Task<bool> RedactNotificationContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
            return false;

        if (notification.ProviderMessageSid is not null && !notification.ContentRedacted)
        {
            // Propagates SmsNotificationException on provider failure — the disposal genuinely did not happen.
            await _sms.RedactContentAsync(notification.ProviderMessageSid, cancellationToken);
        }

        notification.MarkContentRedacted();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<OrderNotification>?> GetOwnedOrderNotificationsAsync(int orderId, string ownerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));

        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null || order.BuyerId != ownerId)
            return null; // one shopper must never see another's order

        return await RefreshAndGetNotificationsAsync(orderId, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderWithNotifications>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var result = new List<OrderWithNotifications>();
        foreach (var order in orders)
        {
            var notifications = await RefreshAndGetNotificationsAsync(order.Id, cancellationToken);
            result.Add(new OrderWithNotifications(order, notifications));
        }

        return result;
    }

    private async Task<IReadOnlyList<OrderNotification>> RefreshAndGetNotificationsAsync(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);

        foreach (var notification in notifications)
        {
            if (notification.ProviderMessageSid is null || IsTerminal(notification.Status))
                continue;

            try
            {
                var state = await _sms.GetDeliveryStateAsync(notification.ProviderMessageSid, cancellationToken);
                notification.UpdateDeliveryState(state.Status, state.ErrorCode, state.ErrorMessage);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (SmsNotificationException ex)
            {
                // A refresh failure must not fail the read — keep the last-known status.
                _logger.LogWarning("Could not refresh delivery state for notification {NotificationId}: {Reason}",
                    notification.Id, ex.Message);
            }
        }

        return notifications;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _sms.ListSentFromConfiguredNumberAsync(from, to, cancellationToken);
        var eShopNotifications = await _notificationRepository.ListAsync(new SentOrderNotificationsInRangeSpecification(from, to), cancellationToken);

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid!)
            .ToDictionary(g => g.Key, g => g.First());
        var eShopBySid = eShopNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationEntry>();
        var eShopOnly = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();

        foreach (var (sid, notification) in eShopBySid)
        {
            if (providerBySid.TryGetValue(sid, out var providerMessage))
                matched.Add(new ReconciliationEntry(sid, providerMessage.Status, providerMessage.DateSent, notification.Id, notification.OrderId));
            else
                eShopOnly.Add(new ReconciliationEntry(sid, notification.Status, null, notification.Id, notification.OrderId));
        }

        foreach (var (sid, providerMessage) in providerBySid)
        {
            if (!eShopBySid.ContainsKey(sid))
                providerOnly.Add(new ReconciliationEntry(sid, providerMessage.Status, providerMessage.DateSent, null, null));
        }

        return new ReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = _sms.ConfiguredFromNumber,
            ProviderMessageCount = providerBySid.Count,
            EShopMessageCount = eShopBySid.Count,
            MatchedCount = matched.Count,
            Matched = matched,
            EShopOnly = eShopOnly,
            ProviderOnly = providerOnly
        };
    }

    // --- helpers -------------------------------------------------------------------------------

    private async Task SendImmediateToShopperAsync(Order order, NotificationKind kind, CancellationToken cancellationToken)
    {
        var numbers = await _contactRepository.ListAsync(new ContactNumbersByOwnerSpecification(order.BuyerId), cancellationToken);
        if (numbers.Count == 0)
        {
            // A shopper with no number on file is simply not messaged.
            _logger.LogInformation("Order {OrderId}: no contact number on file; {Kind} notification skipped.", order.Id, kind);
            return;
        }

        var body = BodyForKind(kind, order.Id);
        foreach (var number in numbers)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, kind, number.PhoneNumber, NotificationStatus.SendFailed);
            try
            {
                var sent = await _sms.SendAsync(number.PhoneNumber, body, cancellationToken);
                notification.MarkSent(sent.ProviderMessageSid, sent.Status);
            }
            catch (SmsNotificationException ex)
            {
                notification.MarkSendFailed(NotificationStatus.SendFailed, ex.Message);
                _logger.LogWarning("Order {OrderId}: {Kind} SMS could not be sent: {Reason}", order.Id, kind, ex.Message);
            }

            await _notificationRepository.AddAsync(notification, cancellationToken);
        }
    }

    private async Task ScheduleFollowUpToShopperAsync(Order order, CancellationToken cancellationToken)
    {
        var numbers = await _contactRepository.ListAsync(new ContactNumbersByOwnerSpecification(order.BuyerId), cancellationToken);
        if (numbers.Count == 0)
            return;

        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var body = BodyForKind(NotificationKind.DeliveryFollowUp, order.Id);
        foreach (var number in numbers)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, NotificationKind.DeliveryFollowUp, number.PhoneNumber, NotificationStatus.ScheduleFailed);
            try
            {
                var scheduled = await _sms.ScheduleAsync(number.PhoneNumber, body, sendAt, cancellationToken);
                notification.MarkSent(scheduled.ProviderMessageSid, scheduled.Status);
            }
            catch (SmsNotificationException ex)
            {
                notification.MarkSendFailed(NotificationStatus.ScheduleFailed, ex.Message);
                _logger.LogWarning("Order {OrderId}: delivery follow-up could not be scheduled: {Reason}", order.Id, ex.Message);
            }

            await _notificationRepository.AddAsync(notification, cancellationToken);
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        foreach (var notification in notifications)
        {
            if (notification.Kind != NotificationKind.DeliveryFollowUp
                || notification.ProviderMessageSid is null
                || !IsCancellable(notification.Status))
            {
                continue;
            }

            try
            {
                await _sms.CancelScheduledAsync(notification.ProviderMessageSid, cancellationToken);
                notification.UpdateDeliveryState("canceled", null, null);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (SmsNotificationException ex)
            {
                _logger.LogWarning("Order {OrderId}: follow-up {NotificationId} could not be cancelled: {Reason}",
                    orderId, notification.Id, ex.Message);
            }
        }
    }

    private static string BodyForKind(NotificationKind kind, int orderId) => kind switch
    {
        NotificationKind.OrderPlaced => $"eShop: your order #{orderId} has been placed. Thank you for shopping with us!",
        NotificationKind.OrderDispatched => $"eShop: good news — your order #{orderId} is on its way!",
        NotificationKind.DeliveryFollowUp => $"eShop: how did the delivery of your order #{orderId} go? We'd love your feedback.",
        NotificationKind.OrderCancelled => $"eShop: your order #{orderId} has been cancelled. If this is unexpected, please contact us.",
        _ => $"eShop: an update about your order #{orderId}."
    };

    private static bool IsTerminal(string status) =>
        status is "delivered" or "undelivered" or "failed" or "canceled"
        or NotificationStatus.SendFailed or NotificationStatus.ScheduleFailed;

    private static bool IsCancellable(string status) =>
        status is "scheduled" or "accepted";
}
