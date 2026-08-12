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

public class OrderMessagingService : IOrderMessagingService
{
    /// <summary>How long after dispatch the "how did the delivery go?" follow-up is queued to go out.</summary>
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IContactNumberService _contactNumberService;
    private readonly ISmsService _smsService;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderMessagingService> _logger;

    public OrderMessagingService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<OrderNotification> notificationRepository,
        IContactNumberService contactNumberService,
        ISmsService smsService,
        IUriComposer uriComposer,
        IAppLogger<OrderMessagingService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _notificationRepository = notificationRepository;
        _contactNumberService = contactNumberService;
        _smsService = smsService;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    // ---------------------------------------------------------------- place / dispatch / cancel

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> items, Address? shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        if (items is null || items.Count == 0)
            throw new InvalidOrderRequestException("An order must contain at least one item.");
        if (items.Any(i => i.Quantity <= 0))
            throw new InvalidOrderRequestException("Every item quantity must be greater than zero.");

        var requestedIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(requestedIds), cancellationToken);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var missing = requestedIds.Where(id => !byId.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOrderRequestException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");

        var orderItems = items.Select(line =>
        {
            var catalogItem = byId[line.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var address = shipToAddress ?? new Address("N/A", "N/A", "N/A", "N/A", "N/A");
        var order = new Order(buyerId, address, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);

        await NotifyBuyerAsync(order, NotificationType.OrderPlaced,
            $"Thanks! Your eShop order #{order.Id} has been placed. We'll text you when it ships.",
            cancellationToken);

        return order;
    }

    public async Task<Order?> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
            return null;

        order.MarkDispatched();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        await NotifyBuyerAsync(order, NotificationType.OrderDispatched,
            $"Good news — your eShop order #{order.Id} is on its way!",
            cancellationToken);

        await ScheduleDeliveryFollowUpAsync(order, cancellationToken);

        return order;
    }

    public async Task<Order?> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
            return null;

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        // Critical: call off any not-yet-sent follow-up so it can never reach the shopper.
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        await NotifyBuyerAsync(order, NotificationType.OrderCancelled,
            $"Your eShop order #{order.Id} has been cancelled. If this is unexpected, please contact support.",
            cancellationToken);

        return order;
    }

    // ---------------------------------------------------------------- shopper reads

    public async Task<IReadOnlyList<OrderWithNotifications>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);

        var result = new List<OrderWithNotifications>(orders.Count);
        foreach (var order in orders)
        {
            var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(order.Id), cancellationToken);
            await RefreshDeliveryOutcomesAsync(notifications, cancellationToken);
            result.Add(new OrderWithNotifications(order, notifications));
        }

        return result;
    }

    public async Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null || order.BuyerId != buyerId)
            return null;

        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshDeliveryOutcomesAsync(notifications, cancellationToken);
        return notifications;
    }

    // ---------------------------------------------------------------- operator actions

    public async Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(idempotencyKey, nameof(idempotencyKey));

        // A repeat under the same key must not send a second message.
        var priorForKey = await _notificationRepository.ListAsync(new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (priorForKey.Count > 0)
            return priorForKey[0];

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
            return null;

        if (original.ContentDisposed || string.IsNullOrEmpty(original.Body))
            throw new InvalidOrderRequestException("This message has no content to re-send (it was disposed of).");
        if (original.IsScheduled)
            throw new InvalidOrderRequestException("A scheduled message cannot be re-sent.");

        var resend = OrderNotification.Create(original.OrderId, original.BuyerId, original.Type, original.ToPhoneNumber, original.Body);
        resend.SetIdempotencyKey(idempotencyKey);
        await _notificationRepository.AddAsync(resend, cancellationToken);

        try
        {
            var sent = await _smsService.SendAsync(resend.ToPhoneNumber, original.Body, cancellationToken);
            resend.MarkSent(sent.Sid, sent.Status);
        }
        catch (SmsProviderException ex)
        {
            _logger.LogWarning("Resend of notification {NotificationId} for order {OrderId} failed at the provider (status {Status}).",
                notificationId, resend.OrderId, ex.ProviderStatusCode as object ?? "n/a");
            resend.MarkSendFailed(null, "The messaging provider rejected the message.");
        }

        await _notificationRepository.UpdateAsync(resend, cancellationToken);
        return resend;
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
            return false;

        // The text must no longer be retrievable from the provider either — so redact there first, and only
        // clear it locally once that has actually succeeded.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            await _smsService.RedactMessageBodyAsync(notification.ProviderMessageSid, cancellationToken);
        }

        notification.DisposeContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerRecords = await _smsService.ListSentMessagesAsync(from, to, cancellationToken);
        var localSent = await _notificationRepository.ListAsync(new SentOrderNotificationsInRangeSpecification(from, to), cancellationToken);

        var providerBySid = providerRecords
            .Where(r => !string.IsNullOrEmpty(r.Sid))
            .GroupBy(r => r.Sid)
            .ToDictionary(g => g.Key, g => g.First());
        var localBySid = localSent
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var entries = new List<ReconciliationEntry>();

        foreach (var provider in providerBySid.Values)
        {
            if (localBySid.TryGetValue(provider.Sid, out var local))
            {
                entries.Add(new ReconciliationEntry(provider.Sid, ReconciliationOutcome.Matched,
                    provider.Status, local.Status, local.Id, local.OrderId));
            }
            else
            {
                entries.Add(new ReconciliationEntry(provider.Sid, ReconciliationOutcome.MissingInEShop,
                    provider.Status, null, null, null));
            }
        }

        foreach (var local in localBySid.Values)
        {
            if (!providerBySid.ContainsKey(local.ProviderMessageSid!))
            {
                entries.Add(new ReconciliationEntry(local.ProviderMessageSid!, ReconciliationOutcome.MissingAtProvider,
                    null, local.Status, local.Id, local.OrderId));
            }
        }

        var matched = entries.Count(e => e.Outcome == ReconciliationOutcome.Matched);
        var missingInEShop = entries.Count(e => e.Outcome == ReconciliationOutcome.MissingInEShop);
        var missingAtProvider = entries.Count(e => e.Outcome == ReconciliationOutcome.MissingAtProvider);

        return new ReconciliationReport(from, to, providerBySid.Count, localBySid.Count,
            matched, missingInEShop, missingAtProvider, entries);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Send one notification to every number the shopper currently has on file. Best-effort: a send that
    /// cannot go out is recorded as failed and never propagates to fail the order operation.
    /// </summary>
    private async Task NotifyBuyerAsync(Order order, NotificationType type, string body, CancellationToken cancellationToken)
    {
        IReadOnlyList<Entities.ContactNumberAggregate.ContactNumber> numbers;
        try
        {
            numbers = await _contactNumberService.ListAsync(order.BuyerId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not load contact numbers for order {OrderId}; skipping {Type} notification. {Error}",
                order.Id, type, ex.Message);
            return;
        }

        foreach (var number in numbers)
        {
            var notification = OrderNotification.Create(order.Id, order.BuyerId, type, number.PhoneNumber, body);
            var persisted = false;
            try
            {
                await _notificationRepository.AddAsync(notification, cancellationToken);
                persisted = true;

                var sent = await _smsService.SendAsync(number.PhoneNumber, body, cancellationToken);
                notification.MarkSent(sent.Sid, sent.Status);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (SmsProviderException ex)
            {
                _logger.LogWarning("Could not send {Type} SMS for order {OrderId} (provider status {Status}).",
                    type, order.Id, ex.ProviderStatusCode as object ?? "n/a");
                await RecordSendFailureAsync(notification, persisted, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Unexpected error sending {Type} SMS for order {OrderId}. {Error}",
                    type, order.Id, ex.Message);
                await RecordSendFailureAsync(notification, persisted, cancellationToken);
            }
        }
    }

    private async Task ScheduleDeliveryFollowUpAsync(Order order, CancellationToken cancellationToken)
    {
        IReadOnlyList<Entities.ContactNumberAggregate.ContactNumber> numbers;
        try
        {
            numbers = await _contactNumberService.ListAsync(order.BuyerId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not load contact numbers for order {OrderId}; skipping follow-up scheduling. {Error}",
                order.Id, ex.Message);
            return;
        }

        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var body = $"How did the delivery of your eShop order #{order.Id} go? Reply and let us know — we'd love your feedback.";

        foreach (var number in numbers)
        {
            var notification = OrderNotification.Create(order.Id, order.BuyerId, NotificationType.DeliveryFollowUp, number.PhoneNumber, body);
            var persisted = false;
            try
            {
                await _notificationRepository.AddAsync(notification, cancellationToken);
                persisted = true;

                var scheduled = await _smsService.ScheduleAsync(number.PhoneNumber, body, sendAt, cancellationToken);
                notification.MarkScheduled(scheduled.Sid, scheduled.Status, sendAt);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (SmsProviderException ex)
            {
                _logger.LogWarning("Could not schedule delivery follow-up for order {OrderId} (provider status {Status}).",
                    order.Id, ex.ProviderStatusCode as object ?? "n/a");
                await RecordSendFailureAsync(notification, persisted, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Unexpected error scheduling delivery follow-up for order {OrderId}. {Error}",
                    order.Id, ex.Message);
                await RecordSendFailureAsync(notification, persisted, cancellationToken);
            }
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        IReadOnlyList<OrderNotification> pending;
        try
        {
            pending = await _notificationRepository.ListAsync(new PendingFollowUpsByOrderSpecification(orderId), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not load pending follow-ups for order {OrderId}. {Error}", orderId, ex.Message);
            return;
        }

        foreach (var followUp in pending)
        {
            try
            {
                await _smsService.CancelScheduledAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.MarkCanceled();
                await _notificationRepository.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex)
            {
                // Surfaced as a warning, not a failure of the cancel operation — but this is the incident we
                // most want to avoid, so it is logged prominently for follow-up.
                _logger.LogWarning("FAILED to cancel scheduled follow-up (notification {NotificationId}) for cancelled order {OrderId}. {Error}",
                    followUp.Id, orderId, ex.Message);
            }
        }
    }

    /// <summary>Refresh the provider's delivery outcome for any non-terminal, already-sent notification.</summary>
    private async Task RefreshDeliveryOutcomesAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid) || NotificationDeliveryStatus.IsTerminal(notification.Status))
                continue;

            try
            {
                var state = await _smsService.GetMessageAsync(notification.ProviderMessageSid, cancellationToken);
                notification.UpdateDeliveryStatus(state.Status, state.ErrorCode, state.ErrorMessage);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                // A read must not fail because the provider is briefly unavailable; keep the last-known status.
                _logger.LogWarning("Could not refresh delivery status for notification {NotificationId}. {Error}",
                    notification.Id, ex.Message);
            }
        }
    }

    private async Task RecordSendFailureAsync(OrderNotification notification, bool persisted, CancellationToken cancellationToken)
    {
        if (!persisted)
            return;

        try
        {
            notification.MarkSendFailed(null, "The messaging provider did not accept the message.");
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not record send failure for notification {NotificationId}. {Error}", notification.Id, ex.Message);
        }
    }
}
