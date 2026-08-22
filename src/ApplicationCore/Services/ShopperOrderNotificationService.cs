using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ShopperOrderNotificationService : IShopperOrderNotificationService
{
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private static readonly Address DefaultShipTo =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<ResendIdempotencyRecord> _idempotency;
    private readonly ISmsNotificationGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<ShopperOrderNotificationService> _logger;

    public ShopperOrderNotificationService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ShopperContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IRepository<ResendIdempotencyRecord> idempotency,
        ISmsNotificationGateway gateway,
        IUriComposer uriComposer,
        IAppLogger<ShopperOrderNotificationService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _idempotency = idempotency;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CatalogOrderLine> items,
        Address? shipTo,
        CancellationToken cancellationToken)
    {
        if (items is null || items.Count == 0)
            throw new InvalidOrderOperationException("An order must contain at least one item.");

        foreach (var line in items)
        {
            if (line.Quantity <= 0)
                throw new InvalidOrderOperationException("Each item quantity must be greater than zero.");
        }

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        if (catalogItems.Count != ids.Length)
            throw new InvalidOrderOperationException("One or more catalog items were not found.");

        var orderItems = items.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipTo ?? DefaultShipTo, orderItems);
        await _orders.AddAsync(order, cancellationToken);

        await TryNotifyDestinationsAsync(
            order,
            NotificationKind.OrderPlaced,
            OrderNotificationTemplates.For(NotificationKind.OrderPlaced, order.Id),
            sendAt: null,
            relatedNotificationId: null,
            cancellationToken);

        return order;
    }

    public async Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        foreach (var order in orders)
        {
            await RefreshNotificationsAsync(order.Id, cancellationToken);
        }

        return orders;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListNotificationsForShopperOrderAsync(
        string buyerId,
        int orderId,
        CancellationToken cancellationToken)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
            throw new OrderNotFoundException();

        return await RefreshNotificationsAsync(orderId, cancellationToken);
    }

    public async Task DispatchAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await RequireOrderAsync(orderId, cancellationToken);
        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);

        await TryNotifyDestinationsAsync(
            order,
            NotificationKind.OrderDispatched,
            OrderNotificationTemplates.For(NotificationKind.OrderDispatched, order.Id),
            sendAt: null,
            relatedNotificationId: null,
            cancellationToken);

        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        await TryNotifyDestinationsAsync(
            order,
            NotificationKind.DeliveryFollowUp,
            OrderNotificationTemplates.For(NotificationKind.DeliveryFollowUp, order.Id),
            sendAt,
            relatedNotificationId: null,
            cancellationToken);
    }

    public async Task CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await RequireOrderAsync(orderId, cancellationToken);
        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);

        await CancelOutstandingFollowUpsAsync(order, cancellationToken);

        await TryNotifyDestinationsAsync(
            order,
            NotificationKind.OrderCancelled,
            OrderNotificationTemplates.For(NotificationKind.OrderCancelled, order.Id),
            sendAt: null,
            relatedNotificationId: null,
            cancellationToken);
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new InvalidOrderOperationException("An idempotency key is required.");

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new OrderNotificationNotFoundException();

        await RefreshOneAsync(source, cancellationToken);

        var existing = await _idempotency.FirstOrDefaultAsync(
            new ResendIdempotencySpecification(source.Id, idempotencyKey.Trim()),
            cancellationToken);
        if (existing is not null)
        {
            var previous = await _notifications.GetByIdAsync(existing.ResultingNotificationId, cancellationToken)
                ?? throw new OrderNotificationNotFoundException();
            await RefreshOneAsync(previous, cancellationToken);
            return previous;
        }

        if (!OrderNotification.DidNotReachShopper(source.Status))
            throw new InvalidOrderOperationException("Only messages that did not reach the shopper can be resent.");

        var destinations = await DestinationsStillOnFileAsync(source, cancellationToken);
        if (destinations.Count == 0)
            throw new InvalidOrderOperationException("No registered destination is available for a resend.");

        var body = source.Body ?? OrderNotificationTemplates.For(source.Kind, source.OrderId);
        var created = new OrderNotification(source.OrderId, source.BuyerId, NotificationKind.Resend, body, destinations[0]);
        created.AttachRelated(source.Id);
        await _notifications.AddAsync(created, cancellationToken);

        await _idempotency.AddAsync(
            new ResendIdempotencyRecord(source.Id, idempotencyKey.Trim(), created.Id),
            cancellationToken);

        await TrySendOneAsync(created, destinations[0], body, sendAt: null, cancellationToken);
        return created;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new OrderNotificationNotFoundException();

        if (!string.IsNullOrWhiteSpace(notification.ProviderSid))
        {
            try
            {
                var snapshot = await _gateway.RedactBodyAsync(notification.ProviderSid, cancellationToken);
                notification.ApplyProviderState(snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage);
            }
            catch (Exception ex) when (ex is NotificationProviderException or OperationCanceledException)
            {
                _logger.LogWarning(
                    "Provider content disposal failed for notification {NotificationId}: {ExceptionType}",
                    notification.Id,
                    ex.GetType().Name);
                throw;
            }
        }

        notification.RedactLocalContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (to < from)
            throw new InvalidOrderOperationException("The reconciliation range is invalid.");

        var (providerMessages, truncated) = await _gateway.ListSentFromConfiguredNumberAsync(from, to, cancellationToken);
        var local = await _notifications.ListAsync(new NotificationsInDateRangeSpecification(from, to), cancellationToken);

        var localBySid = local
            .Where(n => !string.IsNullOrWhiteSpace(n.ProviderSid))
            .GroupBy(n => n.ProviderSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matched = new List<NotificationReconciliationItem>();
        var providerOnly = new List<NotificationReconciliationItem>();
        var seenSids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var message in providerMessages)
        {
            if (string.IsNullOrWhiteSpace(message.Sid))
                continue;

            seenSids.Add(message.Sid);
            if (localBySid.TryGetValue(message.Sid, out var localRow))
            {
                matched.Add(ToItem(message, localRow.Id, "matched"));
            }
            else
            {
                providerOnly.Add(ToItem(message, null, "provider"));
            }
        }

        var localOnly = local
            .Where(n => string.IsNullOrWhiteSpace(n.ProviderSid) || !seenSids.Contains(n.ProviderSid))
            .Select(n => new NotificationReconciliationItem(
                n.ProviderSid,
                n.Status,
                n.ContentRedacted ? null : n.Body,
                n.CreatedAt.ToString("O"),
                null,
                n.Id,
                "local"))
            .ToList();

        return new NotificationReconciliationReport(from, to, matched, providerOnly, localOnly, truncated);
    }

    private async Task<Order> RequireOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
            throw new OrderNotFoundException();
        return order;
    }

    private async Task<IReadOnlyList<OrderNotification>> RefreshNotificationsAsync(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _notifications.ListAsync(new NotificationsByOrderIdSpecification(orderId), cancellationToken);
        foreach (var notification in notifications)
        {
            await RefreshOneAsync(notification, cancellationToken);
        }

        return notifications;
    }

    private async Task RefreshOneAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(notification.ProviderSid))
            return;

        try
        {
            var snapshot = await _gateway.FetchAsync(notification.ProviderSid, cancellationToken);
            notification.ApplyProviderState(snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage);
            if (notification.ContentRedacted)
                notification.RedactLocalContent();
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex) when (ex is NotificationProviderException)
        {
            _logger.LogWarning(
                "Could not refresh provider state for notification {NotificationId}: {ExceptionType}",
                notification.Id,
                ex.GetType().Name);
        }
    }

    private async Task CancelOutstandingFollowUpsAsync(Order order, CancellationToken cancellationToken)
    {
        var notifications = await _notifications.ListAsync(new NotificationsByOrderIdSpecification(order.Id), cancellationToken);
        foreach (var followUp in notifications.Where(n => n.Kind == NotificationKind.DeliveryFollowUp))
        {
            await RefreshOneAsync(followUp, cancellationToken);
            if (string.IsNullOrWhiteSpace(followUp.ProviderSid))
                continue;
            if (!OrderNotification.IsStillQueuedAtProvider(followUp.Status) &&
                followUp.Status != "scheduled")
                continue;

            try
            {
                var snapshot = await _gateway.CancelScheduledAsync(followUp.ProviderSid, cancellationToken);
                followUp.ApplyProviderState(snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage);
                await _notifications.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex) when (ex is NotificationProviderException)
            {
                _logger.LogWarning(
                    "Could not cancel follow-up notification {NotificationId} for order {OrderId}: {ExceptionType}",
                    followUp.Id,
                    order.Id,
                    ex.GetType().Name);
            }
        }
    }

    private async Task TryNotifyDestinationsAsync(
        Order order,
        NotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        int? relatedNotificationId,
        CancellationToken cancellationToken)
    {
        var destinations = await _contactNumbers.ListAsync(
            new ContactNumbersByBuyerSpecification(order.BuyerId),
            cancellationToken);
        if (destinations.Count == 0)
            return;

        foreach (var destination in destinations)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, kind, body, destination.CanonicalNumber);
            if (relatedNotificationId is not null)
                notification.AttachRelated(relatedNotificationId.Value);

            await _notifications.AddAsync(notification, cancellationToken);
            await TrySendOneAsync(notification, destination.CanonicalNumber, body, sendAt, cancellationToken);
        }
    }

    private async Task TrySendOneAsync(
        OrderNotification notification,
        string destination,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = sendAt is null
                ? await _gateway.SendAsync(destination, body, cancellationToken)
                : await _gateway.ScheduleAsync(destination, body, sendAt.Value, cancellationToken);

            if (string.IsNullOrWhiteSpace(snapshot.Sid))
            {
                notification.MarkSendFailed("The provider accepted the request without an identifier.");
            }
            else
            {
                notification.RecordProviderAccepted(
                    snapshot.Sid,
                    snapshot.Status,
                    snapshot.ErrorCode,
                    snapshot.ErrorMessage,
                    sendAt);
            }
        }
        catch (Exception ex) when (ex is NotificationProviderException or OperationCanceledException)
        {
            _logger.LogWarning(
                "Message send did not complete for notification {NotificationId} kind {Kind}: {ExceptionType}",
                notification.Id,
                notification.Kind,
                ex.GetType().Name);
            notification.MarkSendFailed("The provider did not accept the message.");
        }

        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    private async Task<IReadOnlyList<string>> DestinationsStillOnFileAsync(
        OrderNotification source,
        CancellationToken cancellationToken)
    {
        var current = await _contactNumbers.ListAsync(
            new ContactNumbersByBuyerSpecification(source.BuyerId),
            cancellationToken);
        var numbers = current.Select(c => c.CanonicalNumber).ToList();
        if (!string.IsNullOrWhiteSpace(source.Destination) &&
            numbers.Contains(source.Destination, StringComparer.Ordinal))
        {
            return new[] { source.Destination };
        }

        return numbers;
    }

    private static NotificationReconciliationItem ToItem(
        ProviderMessageSnapshot message,
        int? localId,
        string source) =>
        new(
            message.Sid,
            message.Status,
            message.Body,
            message.DateCreated,
            message.DateSent,
            localId,
            source);
}
