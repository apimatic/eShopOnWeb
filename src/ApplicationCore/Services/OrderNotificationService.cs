using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Sms;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);
    private static readonly Address DefaultShipTo = new("123 Main Street", "Seattle", "WA", "USA", "98101");

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<NotificationResendRecord> _resendRecords;
    private readonly ISmsNotificationGateway _smsGateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IRepository<NotificationResendRecord> resendRecords,
        ISmsNotificationGateway smsGateway,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _resendRecords = resendRecords;
        _smsGateway = smsGateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogOrderLine> lines, CancellationToken cancellationToken = default)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new InvalidOrderStateException("At least one catalog item is required.");
        }

        if (lines.Any(l => l.Quantity <= 0 || l.CatalogItemId <= 0))
        {
            throw new InvalidOrderStateException("Each line must include a catalog item id and a positive quantity.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        if (catalogItems.Count != ids.Length)
        {
            throw new EntityNotFoundException("One or more catalog items were not found.");
        }

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, DefaultShipTo, orderItems);
        order = await _orders.AddAsync(order, cancellationToken);

        await TryNotifyAsync(
            order,
            OrderNotificationKind.OrderPlaced,
            $"eShopOnWeb: your order #{order.Id} has been placed. Total {order.Total():0.00}.",
            sendAt: null,
            cancellationToken);

        return order;
    }

    public async Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        try
        {
            order.MarkDispatched();
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOrderStateException(ex.Message);
        }

        await _orders.UpdateAsync(order, cancellationToken);

        await TryNotifyAsync(
            order,
            OrderNotificationKind.OrderDispatched,
            $"eShopOnWeb: order #{order.Id} is on its way.",
            sendAt: null,
            cancellationToken);

        await TryNotifyAsync(
            order,
            OrderNotificationKind.DeliveryFollowUp,
            $"eShopOnWeb: how did the delivery of order #{order.Id} go?",
            sendAt: DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay),
            cancellationToken);

        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);

        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        await TryNotifyAsync(
            order,
            OrderNotificationKind.OrderCancelled,
            $"eShopOnWeb: order #{order.Id} has been cancelled.",
            sendAt: null,
            cancellationToken);

        return order;
    }

    public async Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var notifications = await _notifications.ListAsync(
            new OrderNotificationsByOrderIdsSpecification(orders.Select(o => o.Id).ToList()),
            cancellationToken);
        await RefreshProviderStateAsync(notifications, cancellationToken);
        return orders;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListBuyerOrderNotificationsAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new EntityNotFoundException("Order was not found.");
        }

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderIdSpecification(orderId), cancellationToken);
        await RefreshProviderStateAsync(notifications, refreshAllWithSid: true, cancellationToken);
        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListNotificationsForOrdersAsync(IReadOnlyCollection<int> orderIds, CancellationToken cancellationToken = default)
    {
        if (orderIds.Count == 0)
        {
            return Array.Empty<OrderNotification>();
        }

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderIdsSpecification(orderIds), cancellationToken);
        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new InvalidOrderStateException("An idempotency key is required.");
        }

        var existing = await _resendRecords.FirstOrDefaultAsync(
            new NotificationResendByKeySpecification(notificationId, idempotencyKey.Trim()),
            cancellationToken);
        if (existing is not null)
        {
            var previous = await _notifications.GetByIdAsync(existing.ResultNotificationId, cancellationToken);
            if (previous is not null)
            {
                return previous;
            }
        }

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken)
                     ?? throw new EntityNotFoundException("Notification was not found.");

        var destinationStillOnFile = await DestinationStillOnFileAsync(source, cancellationToken);
        if (!destinationStillOnFile)
        {
            throw new InvalidOrderStateException("The destination number is no longer on file; nothing will be sent to it.");
        }

        var body = source.ContentDisposed || string.IsNullOrEmpty(source.Body)
            ? ReconstructBody(source)
            : source.Body;

        var resent = new OrderNotification(
            source.OrderId,
            source.BuyerId,
            source.ContactNumberId,
            source.DestinationPhoneNumber,
            OrderNotificationKind.Resend,
            body,
            scheduledSendAt: null,
            sourceNotificationId: source.Id,
            idempotencyKey: idempotencyKey.Trim());

        await ApplySendAsync(resent, sendAt: null, cancellationToken);
        resent = await _notifications.AddAsync(resent, cancellationToken);

        var record = new NotificationResendRecord(source.Id, idempotencyKey.Trim(), resent.Id);
        await _resendRecords.AddAsync(record, cancellationToken);
        return resent;
    }

    public async Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
                           ?? throw new EntityNotFoundException("Notification was not found.");

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            try
            {
                var updated = await _smsGateway.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
                if (updated is not null)
                {
                    notification.ApplyProviderSnapshot(updated.Status ?? notification.ProviderStatus, updated.ErrorCode, updated.Body);
                }
            }
            catch (Exception)
            {
                _logger.LogWarning("Provider content disposal failed for notification {NotificationId}.", notification.Id);
            }
        }

        notification.MarkContentDisposed();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new InvalidOrderStateException("The reconciliation window 'to' must be on or after 'from'.");
        }

        var fromNumber = _smsGateway.FromNumber;
        if (string.IsNullOrWhiteSpace(fromNumber))
        {
            throw new InvalidOrderStateException("Twilio:FromNumber is not configured.");
        }

        var providerMessages = await _smsGateway.ListSentFromAsync(fromNumber, from, to, cancellationToken);
        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var localBySid = new Dictionary<string, OrderNotification>(StringComparer.Ordinal);
        if (providerBySid.Count > 0)
        {
            var matchingLocal = await _notifications.ListAsync(
                new OrderNotificationsByProviderSidsSpecification(providerBySid.Keys.ToList()),
                cancellationToken);
            foreach (var notification in matchingLocal.Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid)))
            {
                localBySid[notification.ProviderMessageSid!] = notification;
            }
        }

        var localInPeriod = await _notifications.ListAsync(
            new OrderNotificationsWithProviderSidInPeriodSpecification(from, to),
            cancellationToken);
        foreach (var notification in localInPeriod.Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid)))
        {
            localBySid.TryAdd(notification.ProviderMessageSid!, notification);
        }

        var matched = new List<ReconciledMessage>();
        var providerOnly = new List<ReconciledMessage>();
        var applicationOnly = new List<ReconciledMessage>();

        foreach (var pair in providerBySid)
        {
            if (localBySid.TryGetValue(pair.Key, out var notification))
            {
                matched.Add(new ReconciledMessage(pair.Key, notification.Id, pair.Value.Status, notification.ProviderStatus));
            }
            else
            {
                providerOnly.Add(new ReconciledMessage(pair.Key, null, pair.Value.Status, null));
            }
        }

        foreach (var pair in localBySid)
        {
            if (!providerBySid.ContainsKey(pair.Key))
            {
                applicationOnly.Add(new ReconciledMessage(pair.Key, pair.Value.Id, null, pair.Value.ProviderStatus));
            }
        }

        return new NotificationReconciliationReport(fromNumber, from, to, matched, providerOnly, applicationOnly);
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        return order ?? throw new EntityNotFoundException("Order was not found.");
    }

    private async Task TryNotifyAsync(
        Order order,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        var destination = await GetActiveDestinationAsync(order.BuyerId, cancellationToken);
        if (destination is null)
        {
            _logger.LogInformation("No contact number on file for order {OrderId}; skipping SMS.", order.Id);
            return;
        }

        var notification = new OrderNotification(
            order.Id,
            order.BuyerId,
            destination.Id,
            destination.PhoneNumber,
            kind,
            body,
            sendAt);

        await ApplySendAsync(notification, sendAt, cancellationToken);

        try
        {
            await _notifications.AddAsync(notification, cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogWarning("Failed to persist notification for order {OrderId}.", order.Id);
        }
    }

    private async Task ApplySendAsync(OrderNotification notification, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        try
        {
            var attempt = await _smsGateway.SendAsync(
                new SendSmsRequest(notification.DestinationPhoneNumber, notification.Body, sendAt),
                cancellationToken);

            if (attempt.Accepted && attempt.Message?.Sid is not null)
            {
                notification.ApplyProviderAcceptance(
                    attempt.Message.Sid,
                    attempt.Message.Status ?? "queued",
                    attempt.Message.ErrorCode);
            }
            else
            {
                notification.MarkSendFailed(attempt.ErrorCode);
                _logger.LogWarning(
                    "SMS was not accepted for order {OrderId} (provider code {Code}).",
                    notification.OrderId,
                    attempt.ErrorCode as object ?? "none");
            }
        }
        catch (Exception)
        {
            notification.MarkSendFailed(null);
            _logger.LogWarning("SMS send threw for order {OrderId}; the order operation will still succeed.", notification.OrderId);
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notifications.ListAsync(new ScheduledFollowUpsByOrderSpecification(orderId), cancellationToken);
        foreach (var notification in followUps)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var updated = await _smsGateway.CancelScheduledAsync(notification.ProviderMessageSid, cancellationToken);
                if (updated is not null)
                {
                    notification.ApplyProviderSnapshot(updated.Status ?? "canceled", updated.ErrorCode, updated.Body);
                    await _notifications.UpdateAsync(notification, cancellationToken);
                }
                else
                {
                    notification.ApplyProviderSnapshot("canceled", null, notification.Body);
                    await _notifications.UpdateAsync(notification, cancellationToken);
                }
            }
            catch (Exception)
            {
                _logger.LogWarning("Failed to cancel scheduled follow-up {NotificationId} for order {OrderId}.", notification.Id, orderId);
            }
        }
    }

    private async Task<ContactNumber?> GetActiveDestinationAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers.FirstOrDefault();
    }

    private async Task<bool> DestinationStillOnFileAsync(OrderNotification source, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(source.BuyerId), cancellationToken);
        return numbers.Any(n => n.PhoneNumber == source.DestinationPhoneNumber);
    }

    private async Task RefreshProviderStateAsync(
        IReadOnlyList<OrderNotification> notifications,
        CancellationToken cancellationToken) =>
        await RefreshProviderStateAsync(notifications, refreshAllWithSid: false, cancellationToken);

    private async Task RefreshProviderStateAsync(
        IReadOnlyList<OrderNotification> notifications,
        bool refreshAllWithSid,
        CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            if (!refreshAllWithSid && OrderNotification.IsTerminalStatus(notification.ProviderStatus))
            {
                continue;
            }

            try
            {
                var snapshot = await _smsGateway.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                if (snapshot is null)
                {
                    continue;
                }

                notification.ApplyProviderSnapshot(snapshot.Status ?? notification.ProviderStatus, snapshot.ErrorCode, snapshot.Body);
                if (notification.ContentDisposed)
                {
                    notification.MarkContentDisposed();
                }

                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception)
            {
                _logger.LogWarning("Failed to refresh provider state for notification {NotificationId}.", notification.Id);
            }
        }
    }

    private static string ReconstructBody(OrderNotification source) =>
        source.Kind switch
        {
            OrderNotificationKind.OrderPlaced => $"eShopOnWeb: your order #{source.OrderId} has been placed.",
            OrderNotificationKind.OrderDispatched => $"eShopOnWeb: order #{source.OrderId} is on its way.",
            OrderNotificationKind.DeliveryFollowUp => $"eShopOnWeb: how did the delivery of order #{source.OrderId} go?",
            OrderNotificationKind.OrderCancelled => $"eShopOnWeb: order #{source.OrderId} has been cancelled.",
            _ => $"eShopOnWeb: update for order #{source.OrderId}."
        };
}
