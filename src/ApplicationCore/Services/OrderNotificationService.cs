using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    // "A follow-up ... asking how the delivery went is queued with the provider for a few days later."
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    // A sensible placeholder used when the caller does not supply a shipping address; the base
    // storefront checkout hard-codes an address in exactly the same way. Shipping is out of scope here.
    private static readonly Func<Address> DefaultShipToAddress =
        () => new Address("123 Main St.", "Kent", "OH", "United States", "44240");

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

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines, Address? shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(lines, nameof(lines));
        if (lines.Count == 0)
            throw new ArgumentException("An order must contain at least one item.", nameof(lines));

        // Reuse the app's existing order/order-item model; build items from catalog items directly.
        var catalogItemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
                throw new ArgumentException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.", nameof(lines));

            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new ArgumentException($"Catalog item {line.CatalogItemId} does not exist.", nameof(lines));

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress ?? DefaultShipToAddress(), orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        // Tell the shopper their order was placed. Best-effort — never fails the placement.
        await NotifyAsync(order, NotificationType.OrderPlaced, BuildBody(NotificationType.OrderPlaced, order.Id), schedule: false, cancellationToken);

        return order.Id;
    }

    public async Task<bool> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
            return false;

        order.MarkDispatched(); // throws InvalidOrderStateException on an invalid transition (e.g. cancelled)
        await _orderRepository.UpdateAsync(order, cancellationToken);

        // Tell the shopper it is on its way, then queue the "how did it go?" follow-up with the provider.
        await NotifyAsync(order, NotificationType.OrderDispatched, BuildBody(NotificationType.OrderDispatched, order.Id), schedule: false, cancellationToken);
        await NotifyAsync(order, NotificationType.DeliveryFollowUp, BuildBody(NotificationType.DeliveryFollowUp, order.Id), schedule: true, cancellationToken);

        return true;
    }

    public async Task<bool> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
            return false;

        order.MarkCancelled(); // throws InvalidOrderStateException if already cancelled
        await _orderRepository.UpdateAsync(order, cancellationToken);

        // Call off any follow-up not yet sent — a "how did delivery go?" for a cancelled order must never arrive.
        await CancelScheduledFollowUpsAsync(order.Id, cancellationToken);

        // Then tell the shopper it was cancelled.
        await NotifyAsync(order, NotificationType.OrderCancelled, BuildBody(NotificationType.OrderCancelled, order.Id), schedule: false, cancellationToken);

        return true;
    }

    public async Task<ResendResult?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
            return null;

        // Idempotency: a repeat under the same key returns the message the first call produced — no second send.
        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (existing is not null)
            return new ResendResult(existing.Id, WasAlreadySent: true);

        // The message text is deterministic from its type and order, so a disposed original can still be re-sent
        // without resurrecting any previously stored copy.
        var body = original.Body ?? BuildBody(original.Type, original.OrderId);

        var resend = new OrderNotification(original.OrderId, original.OwnerId, original.Type, original.ToNumber, body, idempotencyKey: idempotencyKey);
        await SendImmediateAsync(resend, cancellationToken);
        await _notificationRepository.AddAsync(resend, cancellationToken);

        return new ResendResult(resend.Id, WasAlreadySent: false);
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
            return false;

        if (notification.ContentDisposed)
            return true; // already disposed; nothing at the provider to remove, body already cleared

        // The text must no longer be retrievable from the provider either — not merely hidden here.
        if (notification.ProviderMessageSid is not null)
        {
            try
            {
                await _smsProvider.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
            }
            catch (Exception ex)
            {
                // Leave the local copy intact so the operator does not get a false success.
                _logger.LogWarning($"Content disposal at provider failed for notification {notificationId}.");
                throw new NotificationContentDisposalException(notificationId, ex);
            }
        }

        notification.MarkContentDisposed();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        // The caller then reads each order's notifications (which refreshes their live delivery status).
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>?> GetOwnedOrderNotificationsAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null || order.BuyerId != buyerId)
            return null; // not the caller's order — a shopper never sees another's

        return await GetOrderNotificationsAsync(orderId, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshDeliveryStatusesAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var fromNumber = _smsProvider.FromNumber;

        // Ask the provider directly for this number's messages over the range (not a wider list filtered after).
        var providerMessages = await _smsProvider.ListSentFromNumberAsync(fromNumber, from, to, cancellationToken);

        // eShop's own record of what it believes it actually handed to the provider in the same window.
        var localNotifications = await _notificationRepository.ListAsync(
            new OrderNotificationsCreatedBetweenSpecification(from, to), cancellationToken);
        var believedSent = localNotifications
            .Where(n => n.ProviderMessageSid is not null && DeliveryStatuses.IsProviderSendRecord(n.DeliveryStatus))
            .ToList();

        var localBySid = believedSent
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());
        var providerSids = providerMessages.Select(m => m.Sid).ToHashSet();

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        foreach (var message in providerMessages)
        {
            var entry = new ReconciliationEntry(message.Sid, localBySid.TryGetValue(message.Sid, out var n) ? n.Id : null,
                message.Status, Mask(message.To), message.DateSent);
            if (localBySid.ContainsKey(message.Sid))
                matched.Add(entry);
            else
                providerOnly.Add(entry); // the provider knows about it, eShop does not
        }

        var eShopOnly = believedSent
            .Where(n => !providerSids.Contains(n.ProviderMessageSid!))
            .Select(n => new ReconciliationEntry(n.ProviderMessageSid, n.Id, n.DeliveryStatus, Mask(n.ToNumber), null))
            .ToList(); // eShop believes it sent it, the provider's range list does not include it

        return new ReconciliationReport(from, to, fromNumber, matched, providerOnly, eShopOnly);
    }

    // ----- helpers -----------------------------------------------------------------------------

    /// <summary>Creates and sends (or schedules) one notification per number the buyer has on file. Best-effort.</summary>
    private async Task NotifyAsync(Order order, NotificationType type, string body, bool schedule, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(order.BuyerId), cancellationToken);
        if (numbers.Count == 0)
            return; // a shopper with no number on file is simply not messaged

        foreach (var number in numbers)
        {
            var scheduledFor = schedule ? DateTimeOffset.UtcNow.Add(FollowUpDelay) : (DateTimeOffset?)null;
            var notification = new OrderNotification(order.Id, order.BuyerId, type, number.PhoneNumber, body, scheduledFor);

            if (schedule)
                await ScheduleAsync(notification, scheduledFor!.Value, cancellationToken);
            else
                await SendImmediateAsync(notification, cancellationToken);

            await _notificationRepository.AddAsync(notification, cancellationToken);
        }
    }

    private async Task SendImmediateAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _smsProvider.SendAsync(notification.ToNumber, notification.Body!, cancellationToken);
            notification.RecordSendResult(result.Sid, result.Status, result.ErrorCode);
        }
        catch (Exception ex)
        {
            // A message that cannot be sent must never fail the underlying operation.
            notification.RecordSendFailure("provider_send_error");
            _logger.LogWarning($"Could not send {notification.Type} notification for order {notification.OrderId}: {ex.GetType().Name}.");
        }
    }

    private async Task ScheduleAsync(OrderNotification notification, DateTimeOffset sendAt, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _smsProvider.ScheduleAsync(notification.ToNumber, notification.Body!, sendAt, cancellationToken);
            notification.RecordSendResult(result.Sid, result.Status, result.ErrorCode);
        }
        catch (Exception ex)
        {
            notification.RecordSendFailure("provider_schedule_error");
            _logger.LogWarning($"Could not schedule {notification.Type} notification for order {notification.OrderId}: {ex.GetType().Name}.");
        }
    }

    private async Task CancelScheduledFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var scheduled = await _notificationRepository.ListAsync(new ScheduledFollowUpsByOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in scheduled)
        {
            try
            {
                await _smsProvider.CancelScheduledAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.MarkCanceled();
                await _notificationRepository.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex)
            {
                // Never fail the cancel operation, but make the miss visible for follow-up.
                _logger.LogWarning($"Failed to cancel scheduled follow-up notification {followUp.Id} for order {orderId}: {ex.GetType().Name}.");
            }
        }
    }

    /// <summary>Reads the provider's latest status for any notification still in flight and stores it.</summary>
    private async Task RefreshDeliveryStatusesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (notification.ProviderMessageSid is null || !IsRefreshable(notification.DeliveryStatus))
                continue;

            try
            {
                var state = await _smsProvider.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                if (state.Status != notification.DeliveryStatus || state.ErrorCode != notification.ErrorCode)
                {
                    notification.UpdateDeliveryStatus(state.Status, state.ErrorCode);
                    await _notificationRepository.UpdateAsync(notification, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not refresh status for notification {notification.Id}: {ex.GetType().Name}.");
            }
        }
    }

    private static bool IsRefreshable(string status) => status is
        DeliveryStatuses.Accepted or DeliveryStatuses.Queued or DeliveryStatuses.Sending or
        DeliveryStatuses.Sent or DeliveryStatuses.Scheduled;

    private static string BuildBody(NotificationType type, int orderId) => type switch
    {
        NotificationType.OrderPlaced => $"eShop: thanks! Your order #{orderId} has been placed.",
        NotificationType.OrderDispatched => $"eShop: good news — your order #{orderId} is on its way!",
        NotificationType.DeliveryFollowUp => $"eShop: how did the delivery of your order #{orderId} go? We'd love your feedback.",
        NotificationType.OrderCancelled => $"eShop: your order #{orderId} has been cancelled.",
        _ => $"eShop: an update about your order #{orderId}."
    };

    /// <summary>Masks a destination number for operator-facing reports so a shopper's full number is not exposed.</summary>
    private static string? Mask(string? e164)
    {
        if (string.IsNullOrEmpty(e164))
            return e164;
        if (e164.Length <= 4)
            return new string('*', e164.Length);
        return string.Concat(e164.AsSpan(0, 2), new string('*', e164.Length - 4), e164.AsSpan(e164.Length - 2));
    }
}
