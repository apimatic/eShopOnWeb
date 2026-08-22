using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);
    private static readonly Address DefaultShipTo = new("1 eShop Way", "Redmond", "WA", "USA", "98052");

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IUriComposer _uriComposer;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IUriComposer uriComposer,
        ISmsGateway smsGateway,
        IAppLogger<OrderNotificationService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _uriComposer = uriComposer;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogQuantity> items, CancellationToken cancellationToken = default)
    {
        if (items is null || items.Count == 0)
        {
            throw new InvalidOrderStateException("An order must contain at least one catalog item.");
        }

        var quantities = items
            .GroupBy(i => i.CatalogItemId)
            .Select(g => new CatalogQuantity(g.Key, g.Sum(x => x.Quantity)))
            .ToList();

        if (quantities.Any(q => q.Quantity <= 0))
        {
            throw new InvalidOrderStateException("Each catalog item quantity must be greater than zero.");
        }

        var catalogIds = quantities.Select(q => q.CatalogItemId).ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(catalogIds), cancellationToken);
        if (catalogItems.Count != catalogIds.Length)
        {
            throw new EntityNotFoundException("One or more catalog items were not found.");
        }

        var orderItems = quantities.Select(quantity =>
        {
            var catalogItem = catalogItems.First(c => c.Id == quantity.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, quantity.Quantity);
        }).ToList();

        var order = new Order(buyerId, DefaultShipTo, orderItems);
        order = await _orders.AddAsync(order, cancellationToken);

        var body = $"eShop: Your order #{order.Id} has been placed. Total {order.Total().ToString("0.00", CultureInfo.InvariantCulture)}.";
        await TryNotifyAsync(order, OrderNotificationKind.OrderPlaced, body, sendAt: null, cancellationToken);

        return order;
    }

    public async Task DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrderAsync(orderId, cancellationToken);
        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);

        var dispatchedBody = $"eShop: Order #{order.Id} is on its way.";
        await TryNotifyAsync(order, OrderNotificationKind.OrderDispatched, dispatchedBody, sendAt: null, cancellationToken);

        var followUpAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var followUpBody = $"eShop: How did delivery of order #{order.Id} go?";
        await TryNotifyAsync(order, OrderNotificationKind.DeliveryFollowUp, followUpBody, followUpAt, cancellationToken);
    }

    public async Task CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrderAsync(orderId, cancellationToken);
        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);

        await CancelOutstandingFollowUpsAsync(order.Id, cancellationToken);

        var body = $"eShop: Order #{order.Id} has been cancelled.";
        await TryNotifyAsync(order, OrderNotificationKind.OrderCancelled, body, sendAt: null, cancellationToken);
    }

    public async Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListOrderNotificationsAsync(
        int orderId,
        string buyerId,
        bool isAdministrator,
        CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrderAsync(orderId, cancellationToken);
        if (!isAdministrator && !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new EntityNotFoundException("Order not found.");
        }

        var notifications = await _notifications.ListAsync(new NotificationsByOrderIdSpecification(orderId), cancellationToken);
        await RefreshFromProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListNotificationsForOrdersAsync(
        IEnumerable<int> orderIds,
        CancellationToken cancellationToken = default)
    {
        var ids = orderIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return Array.Empty<OrderNotification>();
        }

        var notifications = await _notifications.ListAsync(new NotificationsByOrderIdsSpecification(ids), cancellationToken);
        await RefreshFromProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new InvalidOrderStateException("An idempotency key is required.");
        }

        var existing = await _notifications.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpecification(notificationId, idempotencyKey), cancellationToken);
        if (existing is not null)
        {
            await RefreshFromProviderAsync(new[] { existing }, cancellationToken);
            return existing;
        }

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (source is null)
        {
            throw new EntityNotFoundException("Notification not found.");
        }

        await RefreshFromProviderAsync(new[] { source }, cancellationToken);

        if (source.HasReachedShopper())
        {
            throw new InvalidOrderStateException("That message already reached the shopper.");
        }

        if (source.ContentRedacted || string.IsNullOrWhiteSpace(source.Body))
        {
            throw new InvalidOrderStateException("The original message content is no longer available to resend.");
        }

        var destination = await ResolveActiveDestinationAsync(source.BuyerId, source.DestinationE164, cancellationToken)
            ?? await ResolveActiveDestinationAsync(source.BuyerId, preferred: null, cancellationToken);
        var resend = new OrderNotification(
            source.OrderId,
            source.BuyerId,
            OrderNotificationKind.Resend,
            destination,
            source.Body,
            sourceNotificationId: source.Id,
            idempotencyKey: idempotencyKey);

        resend = await _notifications.AddAsync(resend, cancellationToken);
        await DeliverAsync(resend, sendAt: null, cancellationToken);
        return resend;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            throw new EntityNotFoundException("Notification not found.");
        }

        if (!string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            try
            {
                var redacted = await _smsGateway.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
                if (redacted is not null)
                {
                    notification.ApplyProviderSnapshot(redacted.Status, redacted.ErrorCode, redacted.Body);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to redact provider content for notification {NotificationId}.", notification.Id);
                throw;
            }
        }

        notification.RedactLocalContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new InvalidOrderStateException("The reconciliation window is invalid: 'to' must be on or after 'from'.");
        }

        var providerMessages = await _smsGateway.ListSentFromConfiguredNumberAsync(from, to, cancellationToken);
        var local = await _notifications.ListAsync(new NotificationsInCreatedRangeSpecification(from, to), cancellationToken);

        var localBySid = local
            .Where(n => !string.IsNullOrWhiteSpace(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var providerSids = new HashSet<string>(StringComparer.Ordinal);
        var matched = new List<ReconciliationMatch>();
        var providerOnly = new List<ReconciliationProviderOnly>();

        foreach (var message in providerMessages)
        {
            if (string.IsNullOrWhiteSpace(message.Sid) || !providerSids.Add(message.Sid))
            {
                continue;
            }

            if (localBySid.TryGetValue(message.Sid, out var notification))
            {
                matched.Add(new ReconciliationMatch(notification.Id, message.Sid, message.Status));
            }
            else
            {
                providerOnly.Add(new ReconciliationProviderOnly(message.Sid, message.Status, message.DateCreated));
            }
        }

        var eshopOnly = new List<ReconciliationEshopOnly>();
        foreach (var notification in local)
        {
            if (!string.IsNullOrWhiteSpace(notification.ProviderMessageSid)
                && providerSids.Contains(notification.ProviderMessageSid))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
            {
                try
                {
                    var snapshot = await _smsGateway.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                    if (snapshot is not null && !string.IsNullOrWhiteSpace(snapshot.Sid))
                    {
                        providerSids.Add(snapshot.Sid);
                        matched.Add(new ReconciliationMatch(notification.Id, snapshot.Sid, snapshot.Status));
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to fetch provider record for notification {NotificationId} during reconciliation.", notification.Id);
                }
            }

            eshopOnly.Add(new ReconciliationEshopOnly(notification.Id, notification.ProviderMessageSid, notification.ProviderStatus));
        }

        return new ReconciliationReport(from, to, _smsGateway.FromNumber, matched, providerOnly, eshopOnly);
    }

    private async Task<Order> GetRequiredOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new EntityNotFoundException("Order not found.");
        }

        return order;
    }

    private async Task TryNotifyAsync(
        Order order,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var destination = await ResolveActiveDestinationAsync(order.BuyerId, preferred: null, cancellationToken);
            var notification = new OrderNotification(order.Id, order.BuyerId, kind, destination, body, sendAt);
            notification = await _notifications.AddAsync(notification, cancellationToken);
            await DeliverAsync(notification, sendAt, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Order {OrderId} notification {Kind} failed without affecting the order.", order.Id, kind);
        }
    }

    private async Task DeliverAsync(OrderNotification notification, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(notification.DestinationE164))
        {
            notification.MarkNotSent("skipped_no_destination");
            await _notifications.UpdateAsync(notification, cancellationToken);
            return;
        }

        if (!_smsGateway.IsConfigured)
        {
            notification.MarkNotSent("skipped_not_configured");
            await _notifications.UpdateAsync(notification, cancellationToken);
            return;
        }

        try
        {
            var result = await _smsGateway.SendAsync(notification.DestinationE164, notification.Body ?? string.Empty, sendAt, cancellationToken);
            if (result.Accepted && !string.IsNullOrWhiteSpace(result.ProviderSid))
            {
                notification.ApplyProviderAcceptance(result.ProviderSid, result.Status, result.ErrorCode);
            }
            else
            {
                notification.MarkNotSent(result.Status ?? "failed");
                if (result.ErrorCode is not null)
                {
                    notification.ApplyProviderSnapshot(result.Status ?? "failed", result.ErrorCode, notification.Body);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Provider send failed for notification {NotificationId}.", notification.Id);
            notification.MarkNotSent("failed");
        }

        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    private async Task<string?> ResolveActiveDestinationAsync(string buyerId, string? preferred, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        if (numbers.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(preferred))
        {
            var match = numbers.FirstOrDefault(n => string.Equals(n.PhoneNumber, preferred, StringComparison.Ordinal));
            if (match is not null)
            {
                return match.PhoneNumber;
            }

            return null;
        }

        return numbers[0].PhoneNumber;
    }

    private async Task CancelOutstandingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var outstanding = await _notifications.ListAsync(new OutstandingFollowUpsByOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in outstanding)
        {
            try
            {
                var cancelled = await _smsGateway.CancelAsync(followUp.ProviderMessageSid!, cancellationToken);
                if (cancelled is not null)
                {
                    followUp.ApplyProviderSnapshot(cancelled.Status, cancelled.ErrorCode, cancelled.Body);
                    await _notifications.UpdateAsync(followUp, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cancel scheduled follow-up {NotificationId} for order {OrderId}.", followUp.Id, orderId);
            }
        }
    }

    private async Task RefreshFromProviderAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrWhiteSpace(notification.ProviderMessageSid) || !_smsGateway.IsConfigured)
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

                notification.ApplyProviderSnapshot(snapshot.Status, snapshot.ErrorCode, snapshot.Body);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh provider status for notification {NotificationId}.", notification.Id);
            }
        }
    }
}
