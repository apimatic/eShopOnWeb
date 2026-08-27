using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ShopperOrderService : IShopperOrderService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);
    private static readonly Address DefaultShippingAddress =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<NotificationResendKey> _resendKeys;
    private readonly ITwilioMessagingClient _twilio;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<ShopperOrderService> _logger;

    public ShopperOrderService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ShopperContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IRepository<NotificationResendKey> resendKeys,
        ITwilioMessagingClient twilio,
        IUriComposer uriComposer,
        IAppLogger<ShopperOrderService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _resendKeys = resendKeys;
        _twilio = twilio;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogOrderLine> lines)
    {
        if (lines == null || lines.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one catalog item.");
        }

        if (lines.Any(l => l.Quantity <= 0 || l.CatalogItemId <= 0))
        {
            throw new ArgumentException("Each line must include a catalog item id and a positive quantity.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids));
        if (catalogItems.Count != ids.Length)
        {
            throw new ArgumentException("One or more catalog items were not found.");
        }

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, DefaultShippingAddress, items);
        await _orders.AddAsync(order);

        await TryNotifyAsync(
            order,
            OrderNotificationKind.OrderPlaced,
            $"eShopOnWeb: Your order #{order.Id} has been placed. Thank you for shopping with us.");

        return order;
    }

    public async Task<Order> DispatchAsync(int orderId)
    {
        var order = await GetOrderOrThrow(orderId);
        order.MarkDispatched();
        await _orders.UpdateAsync(order);

        await TryNotifyAsync(
            order,
            OrderNotificationKind.OrderDispatched,
            $"eShopOnWeb: Your order #{order.Id} has been dispatched and is on its way.");

        await TryScheduleFollowUpAsync(order);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId)
    {
        var order = await GetOrderOrThrow(orderId);
        order.MarkCancelled();
        await _orders.UpdateAsync(order);

        await CancelPendingFollowUpsAsync(order.Id);

        await TryNotifyAsync(
            order,
            OrderNotificationKind.OrderCancelled,
            $"eShopOnWeb: Your order #{order.Id} has been cancelled.");

        return order;
    }

    public async Task<IReadOnlyList<ShopperOrderView>> ListMyOrdersAsync(string buyerId)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));
        var orderIds = orders.Select(o => o.Id).ToArray();
        var notifications = orderIds.Length == 0
            ? new List<OrderNotification>()
            : await _notifications.ListAsync(new OrderNotificationsByIdsSpecification(orderIds));

        await RefreshProviderStateAsync(notifications);

        var notificationsByOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(order => ToOrderView(order, notificationsByOrder.GetValueOrDefault(order.Id) ?? new List<OrderNotification>()))
            .ToList();
    }

    public async Task<IReadOnlyList<OrderNotificationView>> ListNotificationsAsync(int orderId, string buyerId, bool isAdministrator)
    {
        var order = await GetOrderOrThrow(orderId);
        if (!isAdministrator && order.BuyerId != buyerId)
        {
            throw new OrderNotFoundException(orderId);
        }

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderIdSpecification(orderId));
        await RefreshProviderStateAsync(notifications);
        return notifications.Select(ToNotificationView).ToList();
    }

    public async Task<ResendNotificationResult> ResendAsync(int notificationId, string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.");
        }

        var existingKey = await _resendKeys.FirstOrDefaultAsync(
            new NotificationResendKeySpecification(notificationId, idempotencyKey.Trim()));
        if (existingKey != null)
        {
            return new ResendNotificationResult(existingKey.ResultNotificationId, true);
        }

        var original = await _notifications.GetByIdAsync(notificationId);
        if (original == null)
        {
            throw new NotificationNotFoundException(notificationId);
        }

        await RefreshProviderStateAsync(new[] { original });

        if (!original.DidNotReachShopper() && !string.IsNullOrEmpty(original.ProviderMessageSid))
        {
            throw new OrderStateException("Only messages that did not reach the shopper can be re-sent.");
        }

        if (original.ContentDisposed || string.IsNullOrEmpty(original.Body))
        {
            throw new OrderStateException("The original message content is no longer available to re-send.");
        }

        var destinations = await ResolveDestinationsForResendAsync(original);
        if (destinations.Count == 0)
        {
            throw new OrderStateException("The shopper has no registered contact number that can receive this message.");
        }

        var destination = destinations[0];
        var resent = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            original.Kind,
            original.Body,
            destination,
            parentNotificationId: original.Id);

        await _notifications.AddAsync(resent);

        var key = new NotificationResendKey(original.Id, idempotencyKey.Trim(), resent.Id);
        await _resendKeys.AddAsync(key);

        await DeliverAsync(resent, schedule: false, sendAt: null);

        return new ResendNotificationResult(resent.Id, false);
    }

    public async Task DisposeContentAsync(int notificationId)
    {
        var notification = await _notifications.GetByIdAsync(notificationId);
        if (notification == null)
        {
            throw new NotificationNotFoundException(notificationId);
        }

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            var result = await _twilio.RedactMessageBodyAsync(notification.ProviderMessageSid);
            if (!result.Succeeded)
            {
                throw new ProviderOperationException("The provider could not dispose of the message content.");
            }
        }

        notification.MarkContentDisposed();
        await _notifications.UpdateAsync(notification);
        _logger.LogInformation("Disposed content for notification {NotificationId}", notificationId);
    }

    public async Task<IReadOnlyList<ReconciliationRow>> ReconcileAsync(DateTimeOffset from, DateTimeOffset to)
    {
        if (to < from)
        {
            throw new ArgumentException("The 'to' timestamp must be on or after 'from'.");
        }

        var providerMessages = await _twilio.ListMessagesFromNumberAsync(_twilio.FromNumber, from, to);
        var local = await _notifications.ListAsync(new OrderNotificationsWithProviderSidSpecification());
        var localBySid = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var rows = new List<ReconciliationRow>();
        var matchedSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in providerMessages)
        {
            if (localBySid.TryGetValue(provider.Sid, out var notification))
            {
                matchedSids.Add(provider.Sid);
                rows.Add(new ReconciliationRow(
                    provider.Sid,
                    provider.Status,
                    provider.DateSent,
                    notification.Id,
                    "matched"));
            }
            else
            {
                rows.Add(new ReconciliationRow(
                    provider.Sid,
                    provider.Status,
                    provider.DateSent,
                    null,
                    "provider_only"));
            }
        }

        foreach (var notification in local)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            if (matchedSids.Contains(notification.ProviderMessageSid))
            {
                continue;
            }

            var inRange = notification.CreatedAt >= from && notification.CreatedAt <= to;
            if (!inRange && notification.ScheduledAt.HasValue)
            {
                inRange = notification.ScheduledAt.Value >= from && notification.ScheduledAt.Value <= to;
            }

            if (!inRange)
            {
                continue;
            }

            rows.Add(new ReconciliationRow(
                notification.ProviderMessageSid,
                notification.ProviderStatus,
                null,
                notification.Id,
                "eshop_only"));
        }

        return rows;
    }

    private async Task<Order> GetOrderOrThrow(int orderId)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId));
        if (order == null)
        {
            throw new OrderNotFoundException(orderId);
        }

        return order;
    }

    private async Task TryNotifyAsync(Order order, OrderNotificationKind kind, string body)
    {
        try
        {
            var destinations = await ListDestinationsAsync(order.BuyerId);
            if (destinations.Count == 0)
            {
                _logger.LogInformation("Skipping {Kind} notification for order {OrderId}; shopper has no contact number", kind, order.Id);
                return;
            }

            foreach (var destination in destinations)
            {
                var notification = new OrderNotification(order.Id, order.BuyerId, kind, body, destination);
                await _notifications.AddAsync(notification);
                await DeliverAsync(notification, schedule: false, sendAt: null);
            }
        }
        catch (Exception)
        {
            _logger.LogWarning("Failed to send {Kind} notification for order {OrderId}; the order operation still succeeded", kind, order.Id);
        }
    }

    private async Task TryScheduleFollowUpAsync(Order order)
    {
        try
        {
            var destinations = await ListDestinationsAsync(order.BuyerId);
            if (destinations.Count == 0)
            {
                return;
            }

            var sendAt = DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay);
            var body = $"eShopOnWeb: How did the delivery of order #{order.Id} go? We would love to hear how it went.";

            foreach (var destination in destinations)
            {
                var notification = new OrderNotification(
                    order.Id,
                    order.BuyerId,
                    OrderNotificationKind.DeliveryFollowUp,
                    body,
                    destination,
                    scheduledAt: sendAt);
                await _notifications.AddAsync(notification);
                await DeliverAsync(notification, schedule: true, sendAt: sendAt);
            }
        }
        catch (Exception)
        {
            _logger.LogWarning("Failed to queue delivery follow-up for order {OrderId}; the dispatch still succeeded", order.Id);
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId)
    {
        try
        {
            var followUps = await _notifications.ListAsync(new ScheduledFollowUpNotificationsSpecification(orderId));
            foreach (var followUp in followUps)
            {
                if (string.IsNullOrEmpty(followUp.ProviderMessageSid))
                {
                    continue;
                }

                var current = await _twilio.FetchMessageAsync(followUp.ProviderMessageSid);
                if (current != null)
                {
                    followUp.ApplyProviderState(current.Status, current.ErrorCode, current.Body);
                }

                if (!string.Equals(followUp.ProviderStatus, "scheduled", StringComparison.OrdinalIgnoreCase))
                {
                    await _notifications.UpdateAsync(followUp);
                    continue;
                }

                var cancel = await _twilio.CancelScheduledMessageAsync(followUp.ProviderMessageSid);
                followUp.ApplyProviderState(cancel.Status, cancel.ErrorCode, followUp.Body);
                await _notifications.UpdateAsync(followUp);
                _logger.LogInformation("Cancelled scheduled follow-up {NotificationId} for order {OrderId}", followUp.Id, orderId);
            }
        }
        catch (Exception)
        {
            _logger.LogWarning("Failed to cancel a scheduled follow-up for order {OrderId}; the cancellation still succeeded", orderId);
        }
    }

    private async Task DeliverAsync(OrderNotification notification, bool schedule, DateTimeOffset? sendAt)
    {
        try
        {
            TwilioSendResult result;
            if (schedule && sendAt.HasValue)
            {
                result = await _twilio.ScheduleMessageAsync(notification.DestinationPhoneNumber, notification.Body ?? string.Empty, sendAt.Value);
            }
            else
            {
                result = await _twilio.SendMessageAsync(notification.DestinationPhoneNumber, notification.Body ?? string.Empty);
            }

            if (result.Succeeded && !string.IsNullOrEmpty(result.Sid))
            {
                notification.RecordProviderAcceptance(result.Sid, result.Status);
            }
            else
            {
                notification.RecordSendFailure(result.ErrorCode, result.Status);
            }
        }
        catch (Exception)
        {
            notification.RecordSendFailure(null);
            _logger.LogWarning("Provider call failed for notification {NotificationId}", notification.Id);
        }

        await _notifications.UpdateAsync(notification);
    }

    private async Task RefreshProviderStateAsync(IEnumerable<OrderNotification> notifications)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _twilio.FetchMessageAsync(notification.ProviderMessageSid);
                if (snapshot == null)
                {
                    continue;
                }

                notification.ApplyProviderState(snapshot.Status, snapshot.ErrorCode, snapshot.Body);
                await _notifications.UpdateAsync(notification);
            }
            catch (Exception)
            {
                _logger.LogWarning("Could not refresh provider state for notification {NotificationId}", notification.Id);
            }
        }
    }

    private async Task<IReadOnlyList<string>> ListDestinationsAsync(string buyerId)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerIdSpecification(buyerId));
        return numbers.Select(n => n.PhoneNumber).ToList();
    }

    private async Task<IReadOnlyList<string>> ResolveDestinationsForResendAsync(OrderNotification original)
    {
        var current = await ListDestinationsAsync(original.BuyerId);
        if (current.Any(n => string.Equals(n, original.DestinationPhoneNumber, StringComparison.Ordinal)))
        {
            return new[] { original.DestinationPhoneNumber };
        }

        return Array.Empty<string>();
    }

    private static ShopperOrderView ToOrderView(Order order, IReadOnlyList<OrderNotification> notifications)
    {
        var items = order.OrderItems
            .Select(i => new ShopperOrderItemView(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units))
            .ToList();

        return new ShopperOrderView(
            order.Id,
            order.Status.ToString(),
            order.OrderDate,
            order.Total(),
            items,
            notifications.Select(ToNotificationView).ToList());
    }

    private static OrderNotificationView ToNotificationView(OrderNotification notification)
    {
        return new OrderNotificationView(
            notification.Id,
            notification.OrderId,
            notification.Kind,
            notification.ProviderStatus,
            notification.ProviderMessageSid,
            notification.ContentDisposed ? null : notification.Body,
            notification.ContentDisposed,
            notification.ErrorCode,
            notification.CreatedAt,
            notification.ScheduledAt,
            notification.ParentNotificationId);
    }
}
