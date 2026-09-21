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
    // How far ahead the "how did delivery go?" follow-up is queued with the provider (within Twilio's 15min–7day window).
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orders;
    private readonly IRepository<Notification> _notifications;
    private readonly IReadRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IUriComposer _uriComposer;
    private readonly ISmsProvider _smsProvider;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orders,
        IRepository<Notification> notifications,
        IReadRepository<ContactNumber> contactNumbers,
        IRepository<CatalogItem> catalogItems,
        IUriComposer uriComposer,
        ISmsProvider smsProvider,
        IAppLogger<OrderNotificationService> logger)
    {
        _orders = orders;
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _catalogItems = catalogItems;
        _uriComposer = uriComposer;
        _smsProvider = smsProvider;
        _logger = logger;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address? shipToAddress, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(lines, nameof(lines));
        if (lines.Count == 0)
            throw new ArgumentException("An order must have at least one item.", nameof(lines));
        if (lines.Any(l => l.Quantity <= 0))
            throw new ArgumentException("Every order line must have a positive quantity.", nameof(lines));

        var catalogItemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);

        var missing = catalogItemIds.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
            throw new ArgumentException($"Unknown catalog item id(s): {string.Join(", ", missing)}", nameof(lines));

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var address = shipToAddress ?? new Address("N/A", "N/A", "N/A", "N/A", "00000");
        var order = new Order(buyerId, address, orderItems);
        order = await _orders.AddAsync(order, cancellationToken);

        _logger.LogInformation("Order {OrderId} placed for buyer.", order.Id);

        await NotifyAsync(order.BuyerId, order.Id, NotificationKind.OrderPlaced, cancellationToken);
        return order.Id;
    }

    public async Task<bool> DispatchAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null) return false;

        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Order {OrderId} dispatched.", orderId);

        // Tell the shopper it is on its way.
        await NotifyAsync(order.BuyerId, order.Id, NotificationKind.OrderDispatched, cancellationToken);

        // Queue the delivery follow-up with the provider for a few days later.
        await ScheduleFollowUpAsync(order.BuyerId, order.Id, cancellationToken);
        return true;
    }

    public async Task<bool> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null) return false;

        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Order {OrderId} cancelled.", orderId);

        // Call off any delivery follow-up that has not yet gone out — a cancelled order must never be
        // asked how its delivery went.
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        // Tell the shopper it was cancelled.
        await NotifyAsync(order.BuyerId, order.Id, NotificationKind.OrderCancelled, cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var orders = await _orders.ListAsync(new CustomerOrdersSpecification(buyerId), cancellationToken);
        if (orders.Count == 0)
            return Array.Empty<OrderWithNotifications>();

        var orderIds = orders.Select(o => o.Id).ToArray();
        var notifications = await _notifications.ListAsync(new NotificationsByOrdersSpecification(orderIds), cancellationToken);

        await RefreshStatusesAsync(notifications, cancellationToken);

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => (IReadOnlyList<Notification>)g.OrderBy(n => n.Id).ToList());

        return orders
            .Select(o => new OrderWithNotifications(o, byOrder.TryGetValue(o.Id, out var ns) ? ns : Array.Empty<Notification>()))
            .ToList();
    }

    public async Task<Order?> GetOrderForBuyerAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null || order.BuyerId != buyerId)
            return null;
        return order;
    }

    public async Task<IReadOnlyList<Notification>> GetOrderNotificationsAsync(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _notifications.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshStatusesAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<Notification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        // Idempotency: a repeat under the same key must not send a second message.
        var priorForKey = await _notifications.ListAsync(new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        var alreadyDone = priorForKey.FirstOrDefault();
        if (alreadyDone is not null)
        {
            _logger.LogInformation("Resend under existing idempotency key returned notification {NotificationId}; nothing sent.", alreadyDone.Id);
            return alreadyDone;
        }

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (source is null)
            return null;

        var body = BuildBody(source.Kind, source.OrderId);
        var resend = new Notification(source.BuyerId, source.OrderId, source.Kind, source.ToNumber, body,
            isScheduled: false, scheduledFor: null, createdAt: DateTimeOffset.UtcNow, idempotencyKey: idempotencyKey);

        var result = await _smsProvider.SendAsync(source.ToNumber, body, cancellationToken);
        ApplyDispatchResult(resend, result);

        resend = await _notifications.AddAsync(resend, cancellationToken);
        _logger.LogInformation("Resent notification {SourceId} as {NotificationId} (sid {Sid}).",
            notificationId, resend.Id, resend.ProviderMessageSid ?? "none");
        return resend;
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
            return false;

        if (notification.ProviderMessageSid is not null && !notification.ContentDisposed)
        {
            // Capture the final outcome before disposing, so "what became of it" survives locally.
            try
            {
                var status = await _smsProvider.FetchStatusAsync(notification.ProviderMessageSid, cancellationToken);
                notification.UpdateDeliveryStatus(status.Status, status.ErrorCode, status.ErrorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not refresh status before disposing notification {NotificationId}: {Reason}", notificationId, ex.Message);
            }

            // Remove the content at the provider so its text is no longer retrievable there.
            await _smsProvider.DeleteContentAsync(notification.ProviderMessageSid, cancellationToken);
        }

        notification.MarkContentDisposed();
        await _notifications.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Disposed content of notification {NotificationId}.", notificationId);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        // Provider side: ask the provider only for messages from this app's configured sending number over the range.
        var providerMessages = await _smsProvider.ListSentMessagesAsync(from, to, cancellationToken);

        // eShop side: what we believe we sent in the range (notifications that reached the provider).
        var storeNotifications = await _notifications.ListAsync(new NotificationsCreatedBetweenSpecification(from, to), cancellationToken);
        var sent = storeNotifications.Where(n => n.ProviderMessageSid is not null).ToList();

        var bySid = sent
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());
        var providerSids = new HashSet<string>(providerMessages.Select(m => m.Sid));

        var matched = new List<ReconciliationMatch>();
        var onlyAtProvider = new List<ReconciliationEntry>();
        foreach (var pm in providerMessages)
        {
            if (bySid.TryGetValue(pm.Sid, out var n))
            {
                matched.Add(new ReconciliationMatch(pm.Sid, pm.Status, n.Id, n.OrderId, n.ProviderStatus));
            }
            else
            {
                onlyAtProvider.Add(new ReconciliationEntry(pm.Sid, pm.Status, null, null, pm.DateSent));
            }
        }

        var onlyInEShop = sent
            .Where(n => !providerSids.Contains(n.ProviderMessageSid!))
            .Select(n => new ReconciliationEntry(n.ProviderMessageSid, n.ProviderStatus, n.Id, n.OrderId, n.CreatedAt))
            .ToList();

        _logger.LogInformation("Reconciliation {From}..{To}: provider={ProviderCount} eShop={EShopCount} matched={Matched} providerOnly={ProviderOnly} eShopOnly={EShopOnly}",
            from, to, providerMessages.Count, sent.Count, matched.Count, onlyAtProvider.Count, onlyInEShop.Count);

        return new ReconciliationReport(from, to, providerMessages.Count, sent.Count, matched, onlyAtProvider, onlyInEShop);
    }

    // --- helpers ---

    private async Task NotifyAsync(string buyerId, int orderId, NotificationKind kind, CancellationToken cancellationToken)
    {
        var toNumber = await GetPrimaryNumberAsync(buyerId, cancellationToken);
        if (toNumber is null)
        {
            _logger.LogInformation("No contact number on file for order {OrderId}; skipping {Kind} notification.", orderId, kind);
            return;
        }

        var body = BuildBody(kind, orderId);
        var notification = new Notification(buyerId, orderId, kind, toNumber, body,
            isScheduled: false, scheduledFor: null, createdAt: DateTimeOffset.UtcNow);

        var result = await _smsProvider.SendAsync(toNumber, body, cancellationToken);
        ApplyDispatchResult(notification, result);

        await _notifications.AddAsync(notification, cancellationToken);
    }

    private async Task ScheduleFollowUpAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var toNumber = await GetPrimaryNumberAsync(buyerId, cancellationToken);
        if (toNumber is null)
            return;

        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var body = BuildBody(NotificationKind.DeliveryFollowUp, orderId);
        var notification = new Notification(buyerId, orderId, NotificationKind.DeliveryFollowUp, toNumber, body,
            isScheduled: true, scheduledFor: sendAt, createdAt: DateTimeOffset.UtcNow);

        var result = await _smsProvider.ScheduleAsync(toNumber, body, sendAt, cancellationToken);
        ApplyDispatchResult(notification, result);

        await _notifications.AddAsync(notification, cancellationToken);
        _logger.LogInformation("Queued delivery follow-up for order {OrderId} (sid {Sid}).", orderId, notification.ProviderMessageSid ?? "none");
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _notifications.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        foreach (var n in notifications.Where(n => n.Kind == NotificationKind.DeliveryFollowUp
                                                   && n.ProviderMessageSid is not null
                                                   && n.ProviderStatus != "canceled"))
        {
            var result = await _smsProvider.CancelScheduledAsync(n.ProviderMessageSid!, cancellationToken);
            if (result.Accepted || string.Equals(result.Status, "canceled", StringComparison.OrdinalIgnoreCase))
            {
                n.MarkCancelledWithProvider();
                await _notifications.UpdateAsync(n, cancellationToken);
                _logger.LogInformation("Called off delivery follow-up {NotificationId} for order {OrderId}.", n.Id, orderId);
            }
            else
            {
                _logger.LogWarning("Could not call off follow-up {NotificationId} for order {OrderId}: {Reason}",
                    n.Id, orderId, result.SendError ?? result.ErrorMessage ?? "unknown");
            }
        }
    }

    private async Task RefreshStatusesAsync(IEnumerable<Notification> notifications, CancellationToken cancellationToken)
    {
        foreach (var n in notifications)
        {
            if (n.ProviderMessageSid is null || n.ContentDisposed)
                continue;
            try
            {
                var status = await _smsProvider.FetchStatusAsync(n.ProviderMessageSid, cancellationToken);
                n.UpdateDeliveryStatus(status.Status, status.ErrorCode, status.ErrorMessage);
                await _notifications.UpdateAsync(n, cancellationToken);
            }
            catch (Exception ex)
            {
                // Best-effort refresh; keep the last known outcome.
                _logger.LogWarning("Could not refresh status for notification {NotificationId}: {Reason}", n.Id, ex.Message);
            }
        }
    }

    private async Task<string?> GetPrimaryNumberAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        // Most recently registered number on file.
        return numbers.OrderByDescending(c => c.Id).FirstOrDefault()?.E164Number;
    }

    private static void ApplyDispatchResult(Notification notification, SmsDispatchResult result)
    {
        if (result.Accepted)
        {
            notification.RecordAccepted(result.ProviderMessageSid!, result.Status);
            notification.UpdateDeliveryStatus(result.Status, result.ErrorCode, result.ErrorMessage);
        }
        else
        {
            notification.RecordSendError(result.SendError ?? result.ErrorMessage ?? "send failed");
        }
    }

    private static string BuildBody(NotificationKind kind, int orderId) => kind switch
    {
        NotificationKind.OrderPlaced => $"eShop: your order #{orderId} has been placed. Thank you for shopping with us!",
        NotificationKind.OrderDispatched => $"eShop: good news! Your order #{orderId} is on its way.",
        NotificationKind.OrderCancelled => $"eShop: your order #{orderId} has been cancelled. If this is unexpected, please contact support.",
        NotificationKind.DeliveryFollowUp => $"eShop: how did the delivery of your order #{orderId} go? We'd love your feedback.",
        _ => $"eShop: an update about your order #{orderId}."
    };
}
