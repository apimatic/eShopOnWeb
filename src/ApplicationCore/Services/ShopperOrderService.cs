using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ShopperOrderService : IShopperOrderService
{
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);
    private static readonly Address DefaultShipTo = new("1 Microsoft Way", "Redmond", "WA", "United States", "98052");

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ISmsGateway _sms;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<ShopperOrderService> _logger;

    public ShopperOrderService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ISmsGateway sms,
        IUriComposer uriComposer,
        IAppLogger<ShopperOrderService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _sms = sms;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<OrderWithNotifications> PlaceAsync(string buyerId, IReadOnlyList<CatalogOrderLine> lines, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new ArgumentException("At least one catalog item is required.", nameof(lines));
        }

        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new ArgumentException("Quantity must be greater than zero.");
            }
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        if (catalogItems.Count != ids.Length)
        {
            throw new ArgumentException("One or more catalog items were not found.");
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, DefaultShipTo, orderItems);
        await _orders.AddAsync(order, cancellationToken);

        await TryNotifyAsync(
            order,
            OrderNotificationKind.OrderPlaced,
            $"eShopOnWeb: your order #{order.Id} has been placed. Total {order.Total():0.00}.",
            scheduleAt: null,
            cancellationToken);

        return await LoadOrderWithNotificationsAsync(order.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderWithNotifications>> ListMineAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        if (orders.Count == 0)
        {
            return Array.Empty<OrderWithNotifications>();
        }

        var notifications = await _notifications.ListAsync(
            new NotificationsByOrderIdsSpecification(orders.Select(o => o.Id)),
            cancellationToken);

        await RefreshStatusesAsync(notifications, cancellationToken);

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => (IReadOnlyList<OrderNotification>)g.ToList());
        return orders
            .Select(o => new OrderWithNotifications(o, byOrder.TryGetValue(o.Id, out var list) ? list : Array.Empty<OrderNotification>()))
            .ToList();
    }

    public async Task<IReadOnlyList<OrderNotification>> ListNotificationsAsync(int orderId, string buyerId, bool isAdministrator, CancellationToken cancellationToken)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || (!isAdministrator && order.BuyerId != buyerId))
        {
            throw new OrderNotFoundException(orderId);
        }

        var notifications = await _notifications.ListAsync(new NotificationsByOrderIdSpecification(orderId), cancellationToken);
        await RefreshStatusesAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<OrderWithNotifications> DispatchAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken)
            ?? throw new OrderNotFoundException(orderId);

        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);

        await TryNotifyAsync(
            order,
            OrderNotificationKind.OrderDispatched,
            $"eShopOnWeb: your order #{order.Id} has been dispatched and is on its way.",
            scheduleAt: null,
            cancellationToken);

        await TryNotifyAsync(
            order,
            OrderNotificationKind.DeliveryFollowUp,
            $"eShopOnWeb: how did delivery go for order #{order.Id}?",
            scheduleAt: DateTimeOffset.UtcNow.Add(FollowUpDelay),
            cancellationToken);

        return await LoadOrderWithNotificationsAsync(order.Id, cancellationToken);
    }

    public async Task<OrderWithNotifications> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken)
            ?? throw new OrderNotFoundException(orderId);

        var existing = await _notifications.ListAsync(new NotificationsByOrderIdSpecification(orderId), cancellationToken);
        foreach (var notification in existing.Where(n => n.IsScheduledPending()))
        {
            try
            {
                var result = await _sms.CancelScheduledAsync(notification.ProviderSid!, cancellationToken);
                notification.ApplyProviderStatus(result.Status, result.ErrorCode, result.ErrorMessage, result.Body);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (SmsProviderException ex)
            {
                _logger.LogWarning("Failed to cancel a scheduled follow-up for order {OrderId}: {Message}", order.Id, ex.Message);
            }
        }

        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);

        await TryNotifyAsync(
            order,
            OrderNotificationKind.OrderCancelled,
            $"eShopOnWeb: your order #{order.Id} has been cancelled.",
            scheduleAt: null,
            cancellationToken);

        return await LoadOrderWithNotificationsAsync(order.Id, cancellationToken);
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new MissingIdempotencyKeyException();
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotificationNotFoundException(notificationId);

        var replay = await _notifications.FirstOrDefaultAsync(
            new ResendNotificationByIdempotencyKeySpecification(notificationId, idempotencyKey),
            cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        await RefreshStatusesAsync(new[] { original }, cancellationToken);

        if (!original.DidNotReachShopper())
        {
            throw new NotificationNotResendableException(notificationId, original.ProviderStatus);
        }

        var stillOnFile = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpecification(original.BuyerId, original.ToNumber),
            cancellationToken);
        if (stillOnFile is null)
        {
            throw new NotificationNotResendableException(notificationId, "destination-removed");
        }

        var body = original.ContentRedacted || string.IsNullOrEmpty(original.Body)
            ? $"eShopOnWeb: a previous update about order #{original.OrderId} did not reach you."
            : original.Body;

        var resend = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            OrderNotificationKind.Resend,
            body,
            original.ToNumber,
            original.FromNumber);
        resend.MarkAsResend(original.Id, idempotencyKey);
        await _notifications.AddAsync(resend, cancellationToken);

        try
        {
            var result = await _sms.SendAsync(original.ToNumber, body, cancellationToken);
            resend.RecordProviderResult(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage, scheduledSendAt: null);
        }
        catch (SmsProviderException)
        {
            resend.RecordSendFailure("The provider could not send the message.");
            _logger.LogWarning("Resend failed for notification {NotificationId} on order {OrderId}.", notificationId, original.OrderId);
        }

        await _notifications.UpdateAsync(resend, cancellationToken);
        return resend;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotificationNotFoundException(notificationId);

        if (notification.ContentRedacted)
        {
            return;
        }

        var originalBody = notification.Body;

        if (!string.IsNullOrEmpty(notification.ProviderSid))
        {
            SmsSendResult result;
            try
            {
                result = await _sms.RedactBodyAsync(notification.ProviderSid, cancellationToken);
            }
            catch (SmsProviderException)
            {
                throw new NotificationContentRedactionException("The provider could not dispose of the message content.");
            }

            if (!string.IsNullOrEmpty(originalBody) &&
                !string.IsNullOrEmpty(result.Body) &&
                string.Equals(result.Body, originalBody, StringComparison.Ordinal))
            {
                throw new NotificationContentRedactionException("The provider still returns the original message text after redaction.");
            }

            notification.ApplyProviderStatus(result.Status, result.ErrorCode, result.ErrorMessage, result.Body);
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var providerMessages = await _sms.ListFromNumberAsync(from, to, cancellationToken);
        var local = await _notifications.ListAsync(new NotificationsByCreatedRangeSpecification(from, to), cancellationToken);

        var localBySid = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderSid))
            .GroupBy(n => n.ProviderSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matches = new List<ReconciliationMatch>();
        var matchedSids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var provider in providerMessages)
        {
            if (string.IsNullOrEmpty(provider.Sid))
            {
                matches.Add(new ReconciliationMatch(null, null, provider.Status, null, "provider-only"));
                continue;
            }

            if (localBySid.TryGetValue(provider.Sid, out var localNotification))
            {
                matchedSids.Add(provider.Sid);
                matches.Add(new ReconciliationMatch(
                    provider.Sid,
                    localNotification.Id,
                    provider.Status,
                    localNotification.ProviderStatus,
                    "matched"));
            }
            else
            {
                matches.Add(new ReconciliationMatch(provider.Sid, null, provider.Status, null, "provider-only"));
            }
        }

        foreach (var localNotification in local)
        {
            if (string.IsNullOrEmpty(localNotification.ProviderSid) || !matchedSids.Contains(localNotification.ProviderSid))
            {
                matches.Add(new ReconciliationMatch(
                    localNotification.ProviderSid,
                    localNotification.Id,
                    null,
                    localNotification.ProviderStatus,
                    "local-only"));
            }
        }

        var matchedCount = matches.Count(m => m.Alignment == "matched");
        var providerOnly = matches.Count(m => m.Alignment == "provider-only");
        var localOnly = matches.Count(m => m.Alignment == "local-only");

        return new ReconciliationReport(from, to, matches, providerMessages.Count, local.Count, matchedCount, providerOnly, localOnly);
    }

    private async Task<OrderWithNotifications> LoadOrderWithNotificationsAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken)
            ?? throw new OrderNotFoundException(orderId);
        var notifications = await _notifications.ListAsync(new NotificationsByOrderIdSpecification(orderId), cancellationToken);
        return new OrderWithNotifications(order, notifications);
    }

    private async Task TryNotifyAsync(
        Order order,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset? scheduleAt,
        CancellationToken cancellationToken)
    {
        var contact = await GetPreferredContactAsync(order.BuyerId, cancellationToken);
        if (contact is null)
        {
            return;
        }

        var notification = new OrderNotification(order.Id, order.BuyerId, kind, body, contact.CanonicalNumber, _sms.FromNumber);
        await _notifications.AddAsync(notification, cancellationToken);

        try
        {
            SmsSendResult result = scheduleAt is null
                ? await _sms.SendAsync(contact.CanonicalNumber, body, cancellationToken)
                : await _sms.ScheduleAsync(contact.CanonicalNumber, body, scheduleAt.Value, cancellationToken);

            notification.RecordProviderResult(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage, scheduleAt);
        }
        catch (SmsProviderException)
        {
            notification.RecordSendFailure("The provider could not send the message.");
            _logger.LogWarning("Notification {Kind} for order {OrderId} could not be sent.", kind, order.Id);
        }

        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    private async Task<ContactNumber?> GetPreferredContactAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers.OrderByDescending(c => c.CreatedAt).FirstOrDefault();
    }

    private async Task RefreshStatusesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderSid) || IsTerminal(notification.ProviderStatus))
            {
                continue;
            }

            try
            {
                var result = await _sms.FetchAsync(notification.ProviderSid!, cancellationToken);
                notification.ApplyProviderStatus(result.Status, result.ErrorCode, result.ErrorMessage, result.Body);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (SmsProviderException)
            {
                _logger.LogWarning("Could not refresh provider status for notification {NotificationId}.", notification.Id);
            }
        }
    }

    private static bool IsTerminal(string status) =>
        status is "delivered" or "undelivered" or "failed" or "canceled" or "received" or "read" or "send_failed";
}
