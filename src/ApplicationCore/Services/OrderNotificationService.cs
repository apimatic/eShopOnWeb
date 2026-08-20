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

public class OrderNotificationService : IOrderNotificationService, INotificationOperatorService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private static readonly Address DefaultShipToAddress =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<NotificationResend> _resends;
    private readonly IUriComposer _uriComposer;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IRepository<NotificationResend> resends,
        IUriComposer uriComposer,
        ISmsGateway smsGateway,
        IAppLogger<OrderNotificationService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _resends = resends;
        _uriComposer = uriComposer;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task<PlaceOrderResult> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<PlaceOrderItem> items,
        Address? shipToAddress,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(items, nameof(items));
        if (items.Count == 0 || items.Any(i => i.Quantity <= 0 || i.CatalogItemId <= 0))
        {
            throw new NotificationOperationException("An order must include catalog item ids and positive quantities.");
        }

        var catalogItemIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);
        if (catalogItems.Count != catalogItemIds.Length)
        {
            throw new NotificationOperationException("One or more catalog items were not found.");
        }

        var orderItems = items.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress ?? DefaultShipToAddress, orderItems);
        order = await _orders.AddAsync(order, cancellationToken);

        var notifications = await NotifyAsync(
            order,
            NotificationKind.OrderPlaced,
            $"Your eShop order #{order.Id} has been placed. Thank you for shopping with us.",
            cancellationToken);

        return new PlaceOrderResult(order, notifications);
    }

    public async Task<OrderWithNotifications> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);

        var dispatched = await NotifyAsync(
            order,
            NotificationKind.OrderDispatched,
            $"Good news — your eShop order #{order.Id} is on its way.",
            cancellationToken);

        var followUpSendAt = DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay);
        var followUps = await NotifyAsync(
            order,
            NotificationKind.DeliveryFollowUp,
            $"How did your delivery go for eShop order #{order.Id}? We would like to hear how it went.",
            cancellationToken,
            followUpSendAt);

        var notifications = dispatched.Concat(followUps).ToList();
        return new OrderWithNotifications(order, notifications);
    }

    public async Task<OrderWithNotifications> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);

        var followUps = await _notifications.ListAsync(
            new OutstandingFollowUpsByOrderSpecification(order.Id),
            cancellationToken);

        foreach (var followUp in followUps)
        {
            await CancelProviderMessageAsync(followUp, cancellationToken);
        }

        var cancelled = await NotifyAsync(
            order,
            NotificationKind.OrderCancelled,
            $"Your eShop order #{order.Id} has been cancelled.",
            cancellationToken);

        var all = followUps.Concat(cancelled).ToList();
        return new OrderWithNotifications(order, all);
    }

    public async Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        if (orders.Count == 0)
        {
            return Array.Empty<OrderWithNotifications>();
        }

        var notifications = await _notifications.ListAsync(
            new NotificationsByOrderIdsSpecification(orders.Select(o => o.Id)),
            cancellationToken);

        await RefreshProviderStateAsync(notifications, cancellationToken);

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(order => new OrderWithNotifications(
                order,
                notifications.Where(n => n.OrderId == order.Id).ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(
        string buyerId,
        int orderId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var order = await GetOrderAsync(orderId, cancellationToken);
        if (order.BuyerId != buyerId)
        {
            throw new OrderNotFoundException();
        }

        var notifications = await _notifications.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshProviderStateAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(
        int notificationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotificationNotFoundException();

        var existing = await _resends.FirstOrDefaultAsync(
            new NotificationResendByKeySpecification(notificationId, idempotencyKey),
            cancellationToken);
        if (existing is not null)
        {
            var prior = await _notifications.GetByIdAsync(existing.ResultNotificationId, cancellationToken);
            if (prior is not null)
            {
                await RefreshProviderStateAsync(new[] { prior }, cancellationToken);
                return prior;
            }
        }

        await RefreshProviderStateAsync(new[] { original }, cancellationToken);

        if (original.ContentDisposed || string.IsNullOrEmpty(original.Body))
        {
            throw new NotificationOperationException("The original message content is no longer available to resend.");
        }

        if (!original.DidNotReachShopper())
        {
            throw new NotificationOperationException("Only messages that did not reach the shopper can be re-sent.");
        }

        var destinationStillOnFile = await DestinationStillOnFileAsync(original, cancellationToken);
        if (!destinationStillOnFile)
        {
            throw new NotificationOperationException("The destination is no longer on file and cannot be messaged again.");
        }

        var resent = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            NotificationKind.Resend,
            original.Body,
            original.DestinationNumber,
            original.ContactNumberId,
            parentNotificationId: original.Id);

        await DeliverAsync(resent, sendAt: null, cancellationToken);
        resent = await _notifications.AddAsync(resent, cancellationToken);

        var resendRecord = new NotificationResend(original.Id, idempotencyKey, resent.Id);
        await _resends.AddAsync(resendRecord, cancellationToken);

        return resent;
    }

    public async Task<OrderNotification> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotificationNotFoundException();

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            SmsSendResult? result = null;
            for (var attempt = 1; attempt <= 4; attempt++)
            {
                try
                {
                    result = await _smsGateway.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
                    if (result.Succeeded)
                    {
                        notification.ApplyProviderSnapshot(result.Status ?? notification.ProviderStatus ?? "unknown", result.ErrorCode, result.ErrorMessage, body: string.Empty);
                        break;
                    }
                }
                catch (Exception)
                {
                    _logger.LogWarning("Failed to redact provider content for notification {NotificationId} (attempt {Attempt}).", notification.Id, attempt);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt), cancellationToken);
            }

            if (result is { Succeeded: false })
            {
                _logger.LogWarning("Provider did not redact content for notification {NotificationId}.", notification.Id);
            }
        }

        notification.MarkContentDisposed();
        await _notifications.UpdateAsync(notification, cancellationToken);
        return notification;
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new NotificationOperationException("The reconciliation window 'to' must be on or after 'from'.");
        }

        var providerMessages = await _smsGateway.ListSentFromAsync(from, to, cancellationToken);
        var eshopNotifications = await _notifications.ListAsync(
            new NotificationsWithProviderSidInRangeSpecification(from, to),
            cancellationToken);

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var eshopBySid = eshopNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var entries = new List<ReconciliationEntry>();

        foreach (var (sid, provider) in providerBySid)
        {
            if (eshopBySid.TryGetValue(sid, out var local))
            {
                entries.Add(new ReconciliationEntry(
                    local.Id.ToString(),
                    sid,
                    local.Kind,
                    local.ProviderStatus,
                    provider.Status,
                    "matched"));
            }
            else
            {
                entries.Add(new ReconciliationEntry(
                    null,
                    sid,
                    "Unknown",
                    null,
                    provider.Status,
                    "providerOnly"));
            }
        }

        foreach (var (sid, local) in eshopBySid)
        {
            if (!providerBySid.ContainsKey(sid))
            {
                entries.Add(new ReconciliationEntry(
                    local.Id.ToString(),
                    sid,
                    local.Kind,
                    local.ProviderStatus,
                    null,
                    "eshopOnly"));
            }
        }

        return new ReconciliationReport(
            from,
            to,
            _smsGateway.SendingNumber,
            entries,
            MatchedCount: entries.Count(e => e.Match == "matched"),
            ProviderOnlyCount: entries.Count(e => e.Match == "providerOnly"),
            EshopOnlyCount: entries.Count(e => e.Match == "eshopOnly"));
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new OrderNotFoundException();
        }

        return order;
    }

    private async Task<IReadOnlyList<OrderNotification>> NotifyAsync(
        Order order,
        string kind,
        string body,
        CancellationToken cancellationToken,
        DateTimeOffset? sendAt = null)
    {
        var destinations = await _contactNumbers.ListAsync(
            new ContactNumbersByBuyerSpecification(order.BuyerId),
            cancellationToken);

        if (destinations.Count == 0)
        {
            return Array.Empty<OrderNotification>();
        }

        var created = new List<OrderNotification>();
        foreach (var destination in destinations)
        {
            var notification = new OrderNotification(
                order.Id,
                order.BuyerId,
                kind,
                body,
                destination.CanonicalNumber,
                destination.Id,
                sendAt);

            await DeliverAsync(notification, sendAt, cancellationToken);
            notification = await _notifications.AddAsync(notification, cancellationToken);
            created.Add(notification);
        }

        return created;
    }

    private async Task DeliverAsync(
        OrderNotification notification,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _smsGateway.SendAsync(
                new SmsSendRequest(notification.DestinationNumber, notification.Body ?? string.Empty, sendAt),
                cancellationToken);

            if (result.Succeeded && !string.IsNullOrEmpty(result.Sid))
            {
                notification.RecordProviderAccepted(result.Sid, result.Status ?? "queued");
            }
            else
            {
                notification.RecordProviderFailure(result.Status, result.ErrorCode, result.ErrorMessage);
                _logger.LogWarning(
                    "Provider did not accept notification {Kind} for order {OrderId}.",
                    notification.Kind,
                    notification.OrderId);
            }
        }
        catch (Exception)
        {
            notification.RecordProviderFailure("failed", null, "The messaging provider could not be reached.");
            _logger.LogWarning(
                "Failed to send notification {Kind} for order {OrderId}. The order operation still succeeded.",
                notification.Kind,
                notification.OrderId);
        }
    }

    private async Task CancelProviderMessageAsync(OrderNotification followUp, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _smsGateway.CancelAsync(followUp.ProviderMessageSid!, cancellationToken);
            if (result.Succeeded)
            {
                followUp.ApplyProviderSnapshot(result.Status ?? "canceled", result.ErrorCode, result.ErrorMessage, body: null);
            }
            else
            {
                followUp.RecordProviderFailure(result.Status, result.ErrorCode, result.ErrorMessage);
                _logger.LogWarning(
                    "Provider did not cancel follow-up notification {NotificationId} for order {OrderId}.",
                    followUp.Id,
                    followUp.OrderId);
            }
        }
        catch (Exception)
        {
            _logger.LogWarning(
                "Failed to cancel follow-up notification {NotificationId} for order {OrderId}.",
                followUp.Id,
                followUp.OrderId);
        }

        await _notifications.UpdateAsync(followUp, cancellationToken);
    }

    private async Task RefreshProviderStateAsync(
        IReadOnlyList<OrderNotification> notifications,
        CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
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

                notification.ApplyProviderSnapshot(
                    snapshot.Status ?? notification.ProviderStatus ?? "unknown",
                    snapshot.ErrorCode,
                    snapshot.ErrorMessage,
                    notification.ContentDisposed ? string.Empty : snapshot.Body);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception)
            {
                _logger.LogWarning(
                    "Could not refresh provider state for notification {NotificationId}.",
                    notification.Id);
            }
        }
    }

    private async Task<bool> DestinationStillOnFileAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(
            new ContactNumbersByBuyerSpecification(notification.BuyerId),
            cancellationToken);

        return numbers.Any(n =>
            n.Id == notification.ContactNumberId
            || string.Equals(n.CanonicalNumber, notification.DestinationNumber, StringComparison.Ordinal));
    }
}
