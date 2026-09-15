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
using Microsoft.eShopWeb.PublicApi.Sms;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public interface IOrderNotificationService
{
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address shipToAddress, CancellationToken ct);
    Task<bool> DispatchAsync(int orderId, CancellationToken ct);
    Task<bool> CancelAsync(int orderId, CancellationToken ct);
    Task<ResendOutcome> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct);
    Task<bool> RedactContentAsync(int notificationId, CancellationToken ct);

    /// <summary>Notifications for one of the caller's own orders, with statuses refreshed from the provider. Null if the order is not the caller's / does not exist.</summary>
    Task<IReadOnlyList<Notification>?> GetOrderNotificationsAsync(string buyerId, int orderId, CancellationToken ct);

    /// <summary>The caller's orders and all their notifications, statuses refreshed from the provider.</summary>
    Task<(IReadOnlyList<Order> Orders, IReadOnlyList<Notification> Notifications)> GetMyOrdersAsync(string buyerId, CancellationToken ct);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

public sealed class OrderNotificationService : IOrderNotificationService
{
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        DeliveryStatuses.Delivered,
        DeliveryStatuses.Undelivered,
        DeliveryStatuses.Failed,
        DeliveryStatuses.Canceled,
        "read"
    };

    private readonly IRepository<Order> _orders;
    private readonly IRepository<Notification> _notifications;
    private readonly IRepository<ContactNumber> _contacts;
    private readonly IRepository<CatalogItem> _items;
    private readonly IUriComposer _uriComposer;
    private readonly ISmsGateway _gateway;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orders,
        IRepository<Notification> notifications,
        IRepository<ContactNumber> contacts,
        IRepository<CatalogItem> items,
        IUriComposer uriComposer,
        ISmsGateway gateway,
        IOptions<TwilioSettings> settings,
        IAppLogger<OrderNotificationService> logger)
    {
        _orders = orders;
        _notifications = notifications;
        _contacts = contacts;
        _items = items;
        _uriComposer = uriComposer;
        _gateway = gateway;
        _settings = settings.Value;
        _logger = logger;
    }

    // ---- Flow 2: order lifecycle ------------------------------------------------------------

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address shipToAddress, CancellationToken ct)
    {
        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _items.ListAsync(new CatalogItemsSpecification(ids), ct);

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId);
            if (catalogItem is null)
            {
                throw new UnknownCatalogItemException(line.CatalogItemId);
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, orderItems);
        await _orders.AddAsync(order, ct);

        // Tell the shopper their order was placed. A messaging failure must not fail the placement.
        await SendOrderNotificationAsync(order, NotificationType.OrderPlaced, PlacedBody(order), ct);

        return order.Id;
    }

    public async Task<bool> DispatchAsync(int orderId, CancellationToken ct)
    {
        var order = await _orders.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            return false;
        }

        await SendOrderNotificationAsync(order, NotificationType.OrderDispatched, DispatchedBody(order), ct);
        // The "how did delivery go?" follow-up is queued WITH THE PROVIDER for later, not held here.
        await ScheduleFollowUpAsync(order, ct);
        return true;
    }

    public async Task<bool> CancelAsync(int orderId, CancellationToken ct)
    {
        var order = await _orders.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            return false;
        }

        // Call off any follow-up that has not yet gone out: asking how a cancelled order's delivery
        // went is exactly the incident to prevent.
        await CancelScheduledFollowUpsAsync(order, ct);
        await SendOrderNotificationAsync(order, NotificationType.OrderCancelled, CancelledBody(order), ct);
        return true;
    }

    // ---- Flow 3: operator actions -----------------------------------------------------------

    public async Task<ResendOutcome> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct)
    {
        // A repeat under the same key must not send a second message.
        var priorByKey = await _notifications.FirstOrDefaultAsync(new NotificationByIdempotencyKeySpecification(idempotencyKey), ct);
        if (priorByKey is not null)
        {
            return new ResendOutcome(ResendStatus.ReusedIdempotent, priorByKey.Id);
        }

        var source = await _notifications.GetByIdAsync(notificationId, ct);
        if (source is null)
        {
            return new ResendOutcome(ResendStatus.SourceNotFound, 0);
        }

        if (string.IsNullOrEmpty(source.Body))
        {
            // Content was disposed of; there is nothing to re-send.
            return new ResendOutcome(ResendStatus.ContentUnavailable, 0);
        }

        var resend = new Notification(source.BuyerId, source.OrderId, source.Type, source.ToNumber, source.Body);
        resend.SetIdempotencyKey(idempotencyKey);
        try
        {
            var res = await _gateway.SendAsync(source.ToNumber, source.Body, ct);
            resend.RecordAccepted(res.ProviderMessageSid, res.Status, res.ErrorCode, res.ErrorMessage, res.DateSent);
        }
        catch (SmsGatewayException ex)
        {
            // Persist the attempt under the key (so a repeat is idempotent) with the failure recorded.
            resend.RecordSendFailure(ex.Message);
        }

        await _notifications.AddAsync(resend, ct);
        return new ResendOutcome(ResendStatus.Sent, resend.Id);
    }

    public async Task<bool> RedactContentAsync(int notificationId, CancellationToken ct)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, ct);
        if (notification is null)
        {
            return false;
        }

        if (notification.ProviderMessageSid is not null && !notification.ContentRedacted)
        {
            // Dispose of it at the provider FIRST. If that fails, surface it (do not claim success)
            // and leave local content intact so the operator can retry.
            await _gateway.RedactContentAsync(notification.ProviderMessageSid, ct);
        }

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, ct);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var providerMessages = await _gateway.ListSentFromConfiguredNumberAsync(from, to, ct);

        var eShop = await _notifications.ListAsync(new NotificationsCreatedBetweenSpecification(from, to), ct);
        var eShopBySid = new Dictionary<string, Notification>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in eShop)
        {
            if (n.ProviderMessageSid is not null && !eShopBySid.ContainsKey(n.ProviderMessageSid))
            {
                eShopBySid[n.ProviderMessageSid] = n;
            }
        }

        var providerSids = new HashSet<string>(providerMessages.Select(p => p.Sid), StringComparer.OrdinalIgnoreCase);

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        foreach (var p in providerMessages)
        {
            if (eShopBySid.TryGetValue(p.Sid, out var n))
            {
                matched.Add(new ReconciliationEntry(p.Sid, p.Status, n.DeliveryStatus, p.DateSent, n.OrderId));
            }
            else
            {
                // Provider knows about a message eShop has no record of.
                providerOnly.Add(new ReconciliationEntry(p.Sid, p.Status, null, p.DateSent, null));
            }
        }

        var eShopOnly = new List<ReconciliationEntry>();
        foreach (var n in eShop)
        {
            if (n.ProviderMessageSid is not null && !providerSids.Contains(n.ProviderMessageSid))
            {
                // eShop believes it sent a message the provider's own record does not show.
                eShopOnly.Add(new ReconciliationEntry(n.ProviderMessageSid, null, n.DeliveryStatus, n.ProviderDateSent, n.OrderId));
            }
        }

        return new ReconciliationReport(
            from, to,
            providerMessages.Count,
            eShopBySid.Count,
            matched.Count,
            matched,
            providerOnly,
            eShopOnly);
    }

    // ---- Reads ------------------------------------------------------------------------------

    public async Task<IReadOnlyList<Notification>?> GetOrderNotificationsAsync(string buyerId, int orderId, CancellationToken ct)
    {
        var order = await _orders.GetByIdAsync(orderId, ct);
        if (order is null || order.BuyerId != buyerId)
        {
            return null; // not found or not the caller's — indistinguishable to the caller
        }

        var list = await _notifications.ListAsync(new NotificationsByOrderSpecification(orderId), ct);
        await RefreshStatusesAsync(list, ct);
        return list;
    }

    public async Task<(IReadOnlyList<Order> Orders, IReadOnlyList<Notification> Notifications)> GetMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var notifications = await _notifications.ListAsync(new NotificationsByBuyerSpecification(buyerId), ct);
        await RefreshStatusesAsync(notifications, ct);
        return (orders, notifications);
    }

    // ---- Helpers ----------------------------------------------------------------------------

    private async Task SendOrderNotificationAsync(Order order, NotificationType type, string body, CancellationToken ct)
    {
        var numbers = await _contacts.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), ct);
        // No number on file → the shopper is simply not messaged.
        foreach (var number in numbers)
        {
            var notification = new Notification(order.BuyerId, order.Id, type, number.E164Number, body);
            try
            {
                var res = await _gateway.SendAsync(number.E164Number, body, ct);
                notification.RecordAccepted(res.ProviderMessageSid, res.Status, res.ErrorCode, res.ErrorMessage, res.DateSent);
            }
            catch (SmsGatewayException ex)
            {
                // A message that cannot be sent must never fail the underlying operation.
                notification.RecordSendFailure(ex.Message);
            }

            await _notifications.AddAsync(notification, ct);
        }
    }

    private async Task ScheduleFollowUpAsync(Order order, CancellationToken ct)
    {
        var numbers = await _contacts.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), ct);
        var sendAt = DateTimeOffset.Now.AddDays(_settings.FollowUpDelayDays);
        var body = FollowUpBody(order);

        foreach (var number in numbers)
        {
            var notification = new Notification(order.BuyerId, order.Id, NotificationType.DeliveryFollowUp, number.E164Number, body);
            try
            {
                var res = await _gateway.ScheduleAsync(number.E164Number, body, sendAt, ct);
                notification.RecordAccepted(res.ProviderMessageSid, res.Status, res.ErrorCode, res.ErrorMessage, res.DateSent);
            }
            catch (SmsGatewayException ex)
            {
                notification.RecordSendFailure(ex.Message);
            }

            await _notifications.AddAsync(notification, ct);
        }
    }

    private async Task CancelScheduledFollowUpsAsync(Order order, CancellationToken ct)
    {
        var pending = await _notifications.ListAsync(new ScheduledFollowUpsByOrderSpecification(order.Id), ct);
        foreach (var notification in pending)
        {
            if (notification.ProviderMessageSid is null)
            {
                continue;
            }

            try
            {
                var res = await _gateway.CancelScheduledAsync(notification.ProviderMessageSid, ct);
                notification.UpdateDeliveryStatus(res.Status, res.ErrorCode, res.ErrorMessage, res.DateSent);
                notification.MarkCanceled();
            }
            catch (SmsGatewayException)
            {
                // If it already went out the provider rejects the cancel — re-read to record the true
                // state. Cancelling the order must not fail because of this.
                try
                {
                    var latest = await _gateway.FetchAsync(notification.ProviderMessageSid, ct);
                    notification.UpdateDeliveryStatus(latest.Status, latest.ErrorCode, latest.ErrorMessage, latest.DateSent);
                }
                catch (SmsGatewayException)
                {
                    // Keep last-known status.
                }
            }

            await _notifications.UpdateAsync(notification, ct);
        }
    }

    private async Task RefreshStatusesAsync(IReadOnlyList<Notification> notifications, CancellationToken ct)
    {
        foreach (var notification in notifications)
        {
            if (notification.ProviderMessageSid is null || TerminalStatuses.Contains(notification.DeliveryStatus))
            {
                continue;
            }

            try
            {
                var latest = await _gateway.FetchAsync(notification.ProviderMessageSid, ct);
                var before = notification.DeliveryStatus;
                notification.UpdateDeliveryStatus(latest.Status, latest.ErrorCode, latest.ErrorMessage, latest.DateSent);
                if (!string.Equals(before, notification.DeliveryStatus, StringComparison.OrdinalIgnoreCase))
                {
                    await _notifications.UpdateAsync(notification, ct);
                }
            }
            catch (SmsGatewayException)
            {
                // Reads must never fail because the provider was momentarily unreachable — keep last-known.
            }
        }
    }

    private static string PlacedBody(Order order) =>
        $"eShop: your order #{order.Id} has been placed. We'll text you when it ships.";

    private static string DispatchedBody(Order order) =>
        $"eShop: good news — your order #{order.Id} is on its way!";

    private static string FollowUpBody(Order order) =>
        $"eShop: how did the delivery of order #{order.Id} go? We'd love your feedback.";

    private static string CancelledBody(Order order) =>
        $"eShop: your order #{order.Id} has been cancelled. No delivery will be made.";
}
