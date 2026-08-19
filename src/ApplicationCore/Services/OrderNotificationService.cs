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
    /// <summary>How far out the "how did the delivery go?" follow-up is queued with the provider.</summary>
    public static readonly TimeSpan DeliveryFeedbackDelay = TimeSpan.FromDays(3);

    private static readonly HashSet<string> TerminalStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "delivered", "undelivered", "failed", "canceled", "cancelled" };

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ISmsGateway _smsGateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        ISmsGateway smsGateway,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _smsGateway = smsGateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    // ---- Flow 2: place / dispatch / cancel ---------------------------------------------------

    public async Task<int> PlaceOrderAsync(string ownerId, IReadOnlyList<OrderLine> lines, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.Null(lines, nameof(lines));
        if (lines.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one line.", nameof(lines));
        }

        var itemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(itemIds), cancellationToken);

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new ArgumentException($"Quantity for catalog item {line.CatalogItemId} must be positive.", nameof(lines));
            }
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new ArgumentException($"Catalog item {line.CatalogItemId} does not exist.", nameof(lines));

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        // Reuse the app's existing Order/OrderItem model. The buyer is the caller.
        var order = new Order(ownerId, EmptyAddress(), orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        await NotifyAsync(order, NotificationKind.OrderPlaced, id => $"eShop: thanks! Your order #{id} has been placed.", cancellationToken);

        return order.Id;
    }

    public async Task<bool> DispatchAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return false;
        }

        // Tell the shopper it is on its way...
        await NotifyAsync(order, NotificationKind.OrderDispatched, id => $"eShop: good news — your order #{id} is on its way!", cancellationToken);

        // ...and queue the "how did it go?" follow-up with the provider for a few days later.
        var sendAt = DateTimeOffset.UtcNow.Add(DeliveryFeedbackDelay);
        await ScheduleFollowUpAsync(order, sendAt, cancellationToken);

        return true;
    }

    public async Task<bool> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return false;
        }

        // Call off any follow-up that has not yet gone out, so a cancelled order never triggers a
        // "how did the delivery go?" message.
        await CancelPendingFollowUpsAsync(orderId, cancellationToken);

        await NotifyAsync(order, NotificationKind.OrderCancelled, id => $"eShop: your order #{id} has been cancelled.", cancellationToken);

        return true;
    }

    // ---- Reads --------------------------------------------------------------------------------

    public async Task<IReadOnlyList<MyOrderView>> GetMyOrdersAsync(string ownerId, CancellationToken cancellationToken)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(ownerId), cancellationToken);
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOwnerSpecification(ownerId), cancellationToken);
        await RefreshStatusesAsync(notifications, cancellationToken);

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => new MyOrderView(
                o.Id,
                o.OrderDate,
                o.Total(),
                o.OrderItems.Select(i => new MyOrderItemView(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units)).ToList(),
                (byOrder.TryGetValue(o.Id, out var ns) ? ns : new List<OrderNotification>()).Select(ToView).ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<OrderNotificationView>?> GetOrderNotificationsAsync(string ownerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null || !string.Equals(order.BuyerId, ownerId, StringComparison.Ordinal))
        {
            // Not the caller's order (or none) — do not disclose it exists.
            return null;
        }

        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshStatusesAsync(notifications, cancellationToken);
        return notifications.Select(ToView).ToList();
    }

    // ---- Flow 3: resend / dispose content / reconcile ----------------------------------------

    public async Task<int?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrWhiteSpace(idempotencyKey, nameof(idempotencyKey));

        // Idempotency: a repeat under the same key returns the earlier resend without sending again.
        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        var source = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (source is null)
        {
            return null;
        }

        var body = source.Body ?? $"eShop: an update about your order #{source.OrderId}.";
        var resend = OrderNotification.ForImmediate(source.OrderId, source.RecipientOwnerId, source.Kind, body);
        resend.MarkIdempotency(idempotencyKey);
        resend.MarkResendOf(source.Id);

        // Persist first so the idempotency key is recorded even if the send throws.
        resend = await _notificationRepository.AddAsync(resend, cancellationToken);

        // Resend to the same destination as the original, provided it still exists and is owned.
        ContactNumber? destination = null;
        if (source.ContactNumberId is int cnId)
        {
            var candidate = await _contactNumberRepository.GetByIdAsync(cnId, cancellationToken);
            if (candidate is not null && string.Equals(candidate.OwnerId, source.RecipientOwnerId, StringComparison.Ordinal))
            {
                destination = candidate;
                resend.AssignDestination(candidate.Id);
            }
        }

        if (destination is not null)
        {
            await TrySendAsync(resend, destination.PhoneNumberE164, cancellationToken);
        }
        else
        {
            _logger.LogWarning($"Resend {resend.Id} of notification {source.Id} had no live destination; nothing sent.");
        }

        await _notificationRepository.UpdateAsync(resend, cancellationToken);
        return resend.Id;
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return false;
        }

        // Redact the body at the provider so the text is no longer retrievable there either. The
        // fact that a message was sent, and what became of it, survives.
        if (notification.ProviderMessageSid is { Length: > 0 } sid)
        {
            try
            {
                await _smsGateway.RedactBodyAsync(sid, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Provider redaction failed for notification {notificationId}: {ex.Message}");
                throw;
            }
        }

        notification.MarkContentDisposed();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var fromNumber = _smsGateway.FromNumber;

        // Ask the provider only for this application's own sending number's messages in range.
        var providerMessages = await _smsGateway.ListMessagesAsync(fromNumber, from, to, cancellationToken);

        var allNotifications = await _notificationRepository.ListAsync(cancellationToken);
        var withSid = allNotifications.Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid)).ToList();
        var eShopSidToNotification = withSid
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // "What eShop believes it sent" is the set of notifications that represent an actual send —
        // a message merely scheduled, or called off before it went out, was never sent.
        var neverSent = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "scheduled", "canceled", "cancelled", OrderNotification.NotSentStatus };
        var eShopInRange = withSid
            .Where(n => n.CreatedAt >= from && n.CreatedAt <= to && !neverSent.Contains(n.DeliveryStatus))
            .ToList();

        var providerSids = new HashSet<string>(providerMessages.Select(m => m.Sid), StringComparer.OrdinalIgnoreCase);

        var matched = new List<ReconciliationEntry>();
        var onlyAtProvider = new List<ReconciliationEntry>();
        foreach (var pm in providerMessages)
        {
            if (eShopSidToNotification.TryGetValue(pm.Sid, out var n))
            {
                matched.Add(new ReconciliationEntry(pm.Sid, n.Id, n.OrderId, n.Kind.ToString(), pm.Status, n.DeliveryStatus, Mask(pm.To), pm.DateSent));
            }
            else
            {
                onlyAtProvider.Add(new ReconciliationEntry(pm.Sid, null, null, null, pm.Status, null, Mask(pm.To), pm.DateSent));
            }
        }

        var onlyInEShop = eShopInRange
            .Where(n => !providerSids.Contains(n.ProviderMessageSid!))
            .Select(n => new ReconciliationEntry(n.ProviderMessageSid, n.Id, n.OrderId, n.Kind.ToString(), null, n.DeliveryStatus, null, null))
            .ToList();

        return new ReconciliationReport(
            from, to, fromNumber,
            providerMessages.Count, eShopInRange.Count, matched.Count,
            matched, onlyAtProvider, onlyInEShop);
    }

    // ---- Helpers ------------------------------------------------------------------------------

    /// <summary>Send one message per contact number on file, recording a notification for each.</summary>
    private async Task NotifyAsync(Order order, NotificationKind kind, Func<int, string> bodyFor, CancellationToken cancellationToken)
    {
        var body = bodyFor(order.Id);
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(order.BuyerId), cancellationToken);

        if (numbers.Count == 0)
        {
            // No number on file: the shopper is simply not messaged. Record the intent for the operator view.
            var record = OrderNotification.ForImmediate(order.Id, order.BuyerId, kind, body);
            await _notificationRepository.AddAsync(record, cancellationToken);
            return;
        }

        foreach (var number in numbers)
        {
            var notification = OrderNotification.ForImmediate(order.Id, order.BuyerId, kind, body);
            notification.AssignDestination(number.Id);
            notification = await _notificationRepository.AddAsync(notification, cancellationToken);
            await TrySendAsync(notification, number.PhoneNumberE164, cancellationToken);
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
    }

    private async Task ScheduleFollowUpAsync(Order order, DateTimeOffset sendAt, CancellationToken cancellationToken)
    {
        var body = $"eShop: how did the delivery of your order #{order.Id} go? We'd love your feedback.";
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(order.BuyerId), cancellationToken);

        foreach (var number in numbers)
        {
            var notification = OrderNotification.ForScheduled(order.Id, order.BuyerId, NotificationKind.DeliveryFeedback, body, sendAt);
            notification.AssignDestination(number.Id);
            notification = await _notificationRepository.AddAsync(notification, cancellationToken);
            try
            {
                var result = await _smsGateway.ScheduleAsync(number.PhoneNumberE164, body, sendAt, cancellationToken);
                notification.MarkAccepted(result.Sid, result.Status, result.ErrorCode);
                _logger.LogInformation($"Scheduled follow-up notification {notification.Id} ({result.Sid}) for order {order.Id} at {sendAt:o}.");
            }
            catch (Exception ex)
            {
                // A message that cannot be scheduled must never fail the dispatch.
                _logger.LogWarning($"Scheduling follow-up for order {order.Id} failed: {ex.Message}");
            }
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        foreach (var n in notifications.Where(n => n.IsScheduled
                                                   && n.ProviderMessageSid is { Length: > 0 }
                                                   && !TerminalStatuses.Contains(n.DeliveryStatus)))
        {
            try
            {
                await _smsGateway.CancelScheduledAsync(n.ProviderMessageSid!, cancellationToken);
                n.UpdateDeliveryStatus("canceled", null);
                _logger.LogInformation($"Cancelled scheduled follow-up {n.Id} ({n.ProviderMessageSid}) for order {orderId}.");
                await _notificationRepository.UpdateAsync(n, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Cancelling scheduled follow-up {n.Id} for order {orderId} failed: {ex.Message}");
            }
        }
    }

    /// <summary>Send a message and fold the outcome into the notification. Never throws.</summary>
    private async Task TrySendAsync(OrderNotification notification, string toE164, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _smsGateway.SendAsync(toE164, notification.Body!, cancellationToken);
            notification.MarkAccepted(result.Sid, result.Status, result.ErrorCode);
            _logger.LogInformation($"Sent notification {notification.Id} ({result.Sid}) for order {notification.OrderId}; status {result.Status}.");
        }
        catch (Exception ex)
        {
            // A message that cannot be sent must never fail the underlying operation.
            _logger.LogWarning($"Sending notification {notification.Id} for order {notification.OrderId} failed: {ex.Message}");
        }
    }

    /// <summary>Refresh non-terminal notifications from the provider so reads show the latest outcome.</summary>
    private async Task RefreshStatusesAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var n in notifications.Where(n => n.ProviderMessageSid is { Length: > 0 }
                                                   && !n.ContentDisposed
                                                   && !TerminalStatuses.Contains(n.DeliveryStatus)))
        {
            try
            {
                var latest = await _smsGateway.GetMessageAsync(n.ProviderMessageSid!, cancellationToken);
                if (latest is not null)
                {
                    n.UpdateDeliveryStatus(latest.Status, latest.ErrorCode);
                    await _notificationRepository.UpdateAsync(n, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Refreshing status for notification {n.Id} failed: {ex.Message}");
            }
        }
    }

    private static OrderNotificationView ToView(OrderNotification n) => new(
        n.Id, n.OrderId, n.Kind.ToString(), n.DeliveryStatus, n.ProviderMessageSid, n.ErrorCode,
        n.IsScheduled, n.ScheduledFor, n.ContentDisposed, n.CreatedAt);

    private static Address EmptyAddress() => new("N/A", "N/A", "N/A", "N/A", "00000");

    /// <summary>Mask a destination number so it is not disclosed in full in the report.</summary>
    private static string? Mask(string? number)
    {
        if (string.IsNullOrEmpty(number))
        {
            return number;
        }
        var last4 = number.Length <= 4 ? number : number[^4..];
        return $"******{last4}";
    }
}
