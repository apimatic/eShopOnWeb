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
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    // A follow-up "how did the delivery go" message is queued with the provider this far after dispatch.
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    // Provider delivery statuses that will not change again — no point refreshing them.
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered", "undelivered", "failed", "canceled", "read"
    };

    private const string ScheduledStatus = "scheduled";

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<ContactNumber> _contactRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ISmsGateway _smsGateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<ContactNumber> contactRepository,
        IRepository<OrderNotification> notificationRepository,
        ISmsGateway smsGateway,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _contactRepository = contactRepository;
        _notificationRepository = notificationRepository;
        _smsGateway = smsGateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(lines, nameof(lines));
        if (lines.Count == 0)
        {
            throw new ArgumentException("An order must have at least one line.", nameof(lines));
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new ArgumentException($"Catalog item {line.CatalogItemId} does not exist.", nameof(lines));
            if (line.Quantity <= 0)
            {
                throw new ArgumentException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.", nameof(lines));
            }
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        // The notification feature does not collect a shipping address; reuse the reference app's placeholder.
        var shipToAddress = new Address("123 Main St.", "Kent", "OH", "United States", "44240");
        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order, ct);

        await NotifyAsync(order, NotificationKind.OrderPlaced, BuildBody(order.Id, NotificationKind.OrderPlaced), scheduledFor: null, ct);

        return order;
    }

    public async Task DispatchAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct)
            ?? throw new OrderNotFoundException(orderId);

        // Change (and persist) the order state first, so a messaging failure can never undo the dispatch.
        order.Dispatch();
        await _orderRepository.UpdateAsync(order, ct);

        await NotifyAsync(order, NotificationKind.OrderDispatched, BuildBody(order.Id, NotificationKind.OrderDispatched), scheduledFor: null, ct);

        // Queue the "how did the delivery go" follow-up WITH THE PROVIDER for a few days later.
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        await NotifyAsync(order, NotificationKind.DeliveryFollowUp, BuildBody(order.Id, NotificationKind.DeliveryFollowUp), scheduledFor: sendAt, ct);
    }

    public async Task CancelAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct)
            ?? throw new OrderNotFoundException(orderId);

        order.Cancel();
        await _orderRepository.UpdateAsync(order, ct);

        // Call off any follow-up the provider still holds — it must never reach a shopper whose order is cancelled.
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), ct);
        foreach (var candidate in notifications.Where(IsCancellableFollowUp))
        {
            try
            {
                await _smsGateway.CancelScheduledAsync(candidate.ProviderMessageSid!, ct);
                // Reload tracked-by-id to persist the state change (the list above is no-tracking).
                var followUp = await _notificationRepository.GetByIdAsync(candidate.Id, ct);
                if (followUp is not null)
                {
                    followUp.UpdateProviderStatus("canceled", null, null);
                    await _notificationRepository.UpdateAsync(followUp, ct);
                }
                _logger.LogInformation("Cancelled scheduled follow-up notification {NotificationId} for order {OrderId}.", candidate.Id, orderId);
            }
            catch (Exception ex)
            {
                // Surface the failure loudly, but do not fail the cancel operation itself.
                _logger.LogWarning("Failed to cancel scheduled follow-up {NotificationId} for order {OrderId}: {Reason}", candidate.Id, orderId, ex.Message);
            }
        }

        await NotifyAsync(order, NotificationKind.OrderCancelled, BuildBody(order.Id, NotificationKind.OrderCancelled), scheduledFor: null, ct);
    }

    public async Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default)
    {
        var orders = await _orderRepository.ListAsync(new BuyerOrdersWithItemsSpecification(buyerId), ct);
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByBuyerSpecification(buyerId), ct);
        await RefreshProviderStatusesAsync(notifications, ct);

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => (IReadOnlyList<OrderNotification>)g.ToList());
        return orders
            .Select(o => new OrderWithNotifications(o, byOrder.TryGetValue(o.Id, out var ns) ? ns : Array.Empty<OrderNotification>()))
            .ToList();
    }

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, string buyerId, CancellationToken ct = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null || order.BuyerId != buyerId)
        {
            // Do not leak the existence of another shopper's order.
            throw new OrderNotFoundException(orderId);
        }

        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), ct);
        await RefreshProviderStatusesAsync(notifications, ct);
        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        // A repeat under the same key must not send a second message.
        var alreadyDone = await _notificationRepository.FirstOrDefaultAsync(
            new OrderNotificationByResendKeySpecification(idempotencyKey), ct);
        if (alreadyDone is not null)
        {
            _logger.LogInformation("Resend idempotency key already satisfied by notification {NotificationId}; not sending again.", alreadyDone.Id);
            return alreadyDone;
        }

        var original = await _notificationRepository.FirstOrDefaultAsync(
            new OrderNotificationByIdSpecification(notificationId), ct)
            ?? throw new NotificationNotFoundException(notificationId);

        // A redacted original has no text to reuse; rebuild a message of the same kind.
        var body = original.Body ?? BuildBody(original.OrderId, original.Kind);

        var resend = new OrderNotification(original.OrderId, original.BuyerId, original.Kind, original.Recipient, body);
        resend.RecordResendKey(idempotencyKey);
        // Persist first to reserve the idempotency key before the send goes out.
        resend = await _notificationRepository.AddAsync(resend, ct);

        await TrySendAsync(resend, schedule: false, scheduledFor: null, ct);
        await _notificationRepository.UpdateAsync(resend, ct);
        return resend;
    }

    public async Task<OrderNotification> DisposeContentAsync(int notificationId, CancellationToken ct = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, ct)
            ?? throw new NotificationNotFoundException(notificationId);

        if (notification.ContentRedacted)
        {
            return notification;
        }

        // Where the provider holds the message, dispose of its content there first. Unlike order messaging,
        // this is the whole point of the request, so a provider failure here is surfaced (not swallowed).
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            await _smsGateway.DisposeContentAsync(notification.ProviderMessageSid!, ct);
        }

        notification.MarkContentDisposed();
        await _notificationRepository.UpdateAsync(notification, ct);
        _logger.LogInformation("Disposed of content for notification {NotificationId}.", notification.Id);
        return notification;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var providerMessages = await _smsGateway.ListSentAsync(from, to, ct);
        var eshopInRange = await _notificationRepository.ListAsync(new OrderNotificationsSentInRangeSpecification(from, to), ct);

        var eshopBySid = eshopInRange
            .Where(n => n.ProviderMessageSid is not null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());
        var providerSids = new HashSet<string>(providerMessages.Select(m => m.Sid));

        var matched = providerMessages
            .Where(m => eshopBySid.ContainsKey(m.Sid))
            .Select(m =>
            {
                var n = eshopBySid[m.Sid];
                return new ReconciliationMatch(n.Id, n.OrderId, m.Sid, m.Status, n.ProviderStatus);
            })
            .ToList();

        var providerOnly = providerMessages.Where(m => !eshopBySid.ContainsKey(m.Sid)).ToList();
        var eshopOnly = eshopInRange.Where(n => !providerSids.Contains(n.ProviderMessageSid!)).ToList();

        return new ReconciliationReport(from, to, providerMessages.Count, eshopInRange.Count, matched, providerOnly, eshopOnly);
    }

    // --- helpers -------------------------------------------------------------------------------------

    /// <summary>
    /// Send (or schedule) one message per registered number for the order's buyer, recording a notification
    /// for each. A send that fails is recorded as failed and never fails the caller's operation.
    /// </summary>
    private async Task NotifyAsync(Order order, NotificationKind kind, string body, DateTimeOffset? scheduledFor, CancellationToken ct)
    {
        var numbers = await _contactRepository.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), ct);
        if (numbers.Count == 0)
        {
            // A shopper with no number on file is simply not messaged.
            _logger.LogInformation("No contact number on file for order {OrderId}; skipping {Kind} notification.", order.Id, kind);
            return;
        }

        foreach (var number in numbers)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, kind, number.PhoneNumber, body, scheduledFor);
            await TrySendAsync(notification, schedule: scheduledFor is not null, scheduledFor, ct);
            await _notificationRepository.AddAsync(notification, ct);
        }
    }

    /// <summary>Attempt a single send/schedule, recording the outcome on the notification. Never throws.</summary>
    private async Task TrySendAsync(OrderNotification notification, bool schedule, DateTimeOffset? scheduledFor, CancellationToken ct)
    {
        try
        {
            var body = notification.Body ?? string.Empty;
            var result = schedule
                ? await _smsGateway.ScheduleAsync(notification.Recipient, body, scheduledFor!.Value, ct)
                : await _smsGateway.SendAsync(notification.Recipient, body, ct);

            if (string.IsNullOrEmpty(result.MessageSid))
            {
                notification.MarkFailed(result.ErrorCode, result.ErrorMessage ?? "Provider returned no message id.");
                _logger.LogWarning("Notification for order {OrderId} ({Kind}) was not accepted by the provider.", notification.OrderId, notification.Kind);
            }
            else
            {
                notification.MarkSent(result.MessageSid!, result.Status, result.ErrorCode, result.ErrorMessage);
                _logger.LogInformation("Notification for order {OrderId} ({Kind}) accepted by provider with status {Status}.", notification.OrderId, notification.Kind, result.Status);
            }
        }
        catch (SmsGatewayException ex)
        {
            notification.MarkFailed(null, ex.Message);
            _logger.LogWarning("Notification for order {OrderId} ({Kind}) failed to send: {Reason}", notification.OrderId, notification.Kind, ex.Message);
        }
        catch (Exception ex)
        {
            notification.MarkFailed(null, "Unexpected error while sending message.");
            _logger.LogWarning("Notification for order {OrderId} ({Kind}) failed unexpectedly: {Reason}", notification.OrderId, notification.Kind, ex.Message);
        }
    }

    /// <summary>
    /// Refresh the provider-owned delivery status of each non-terminal, non-redacted message, in memory,
    /// for the response only — reads do not write. The notifications passed in are no-tracking instances,
    /// so mutating them shapes the response without touching the store. Never throws.
    /// </summary>
    private async Task RefreshProviderStatusesAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken ct)
    {
        foreach (var notification in notifications)
        {
            // Redaction removes the message text, not its delivery status, so a redacted message can still
            // be refreshed. Skip only messages with no provider id or already at a terminal status.
            if (string.IsNullOrEmpty(notification.ProviderMessageSid) ||
                (notification.ProviderStatus is not null && TerminalStatuses.Contains(notification.ProviderStatus)))
            {
                continue;
            }

            try
            {
                var state = await _smsGateway.FetchStateAsync(notification.ProviderMessageSid!, ct);
                // Shape the response from the no-tracking instance...
                notification.UpdateProviderStatus(state.Status, state.ErrorCode, state.ErrorMessage);
                // ...and persist the refreshed status via a tracked-by-id write, so the store stays current
                // and a message that has reached a terminal status is not re-polled on every later read.
                var tracked = await _notificationRepository.GetByIdAsync(notification.Id, ct);
                if (tracked is not null)
                {
                    tracked.UpdateProviderStatus(state.Status, state.ErrorCode, state.ErrorMessage);
                    await _notificationRepository.UpdateAsync(tracked, ct);
                }
            }
            catch (Exception ex)
            {
                // A status refresh must never break a read.
                _logger.LogWarning("Could not refresh provider status for notification {NotificationId}: {Reason}", notification.Id, ex.Message);
            }
        }
    }

    private static bool IsCancellableFollowUp(OrderNotification n) =>
        n.Kind == NotificationKind.DeliveryFollowUp &&
        n.State == NotificationState.Sent &&
        !string.IsNullOrEmpty(n.ProviderMessageSid) &&
        (n.ProviderStatus is null || ScheduledStatus.Equals(n.ProviderStatus, StringComparison.OrdinalIgnoreCase));

    private static string BuildBody(int orderId, NotificationKind kind) => kind switch
    {
        NotificationKind.OrderPlaced => $"eShop: thanks! Your order #{orderId} has been placed.",
        NotificationKind.OrderDispatched => $"eShop: good news - your order #{orderId} is on its way!",
        NotificationKind.DeliveryFollowUp => $"eShop: how did the delivery of order #{orderId} go? We'd love your feedback.",
        NotificationKind.OrderCancelled => $"eShop: your order #{orderId} has been cancelled.",
        _ => $"eShop: an update about your order #{orderId}."
    };
}
