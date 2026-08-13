using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the SMS messages that go out as an order moves. Sending is always best-effort: a message
/// that cannot be handed to the provider is recorded as failed but never throws out of the order operation,
/// so an order is still placed, dispatched or cancelled and the caller's request still succeeds. Destination
/// numbers are never written to logs.
/// </summary>
public class NotificationService : INotificationService
{
    /// <summary>How far after dispatch the "how did the delivery go?" follow-up is queued to go out.</summary>
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    // Delivery outcomes that will not change again — no point asking the provider to refresh them.
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered", "undelivered", "failed", "canceled", "read", OrderNotification.SendFailedStatus
    };

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ISmsProvider _smsProvider;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<NotificationService> _logger;

    public NotificationService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ISmsProvider smsProvider,
        IUriComposer uriComposer,
        IAppLogger<NotificationService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _smsProvider = smsProvider;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, CancellationToken ct = default)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one item.", nameof(lines));
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new ArgumentException("Every item quantity must be greater than zero.", nameof(lines));
        }

        var catalogItemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(catalogItemIds), ct);
        var missing = catalogItemIds.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            throw new ArgumentException($"Unknown catalog item id(s): {string.Join(", ", missing)}.", nameof(lines));
        }

        // Reuse the app's existing Order/OrderItem model rather than a parallel one.
        var items = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, DefaultShipToAddress(), items);
        order = await _orders.AddAsync(order, ct);

        await NotifyBuyerAsync(order.Id, buyerId, NotificationKind.OrderPlaced, OrderPlacedBody(order), ct);
        return order.Id;
    }

    public async Task<bool> DispatchOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            return false;
        }

        // Tell the shopper it is on its way...
        await NotifyBuyerAsync(order.Id, order.BuyerId, NotificationKind.OrderDispatched, OrderDispatchedBody(order.Id), ct);
        // ...and queue the "how did it go?" follow-up WITH the provider for a few days later.
        await ScheduleFollowUpAsync(order.Id, order.BuyerId, ct);
        return true;
    }

    public async Task<bool> CancelOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            return false;
        }

        // Call off any follow-up that has not yet gone out FIRST, so a cancelled order's customer is never
        // asked how their (non-existent) delivery went.
        await CancelScheduledFollowUpsAsync(order.Id, ct);
        await NotifyBuyerAsync(order.Id, order.BuyerId, NotificationKind.OrderCancelled, OrderCancelledBody(order.Id), ct);
        return true;
    }

    public async Task<IReadOnlyList<OrderWithNotifications>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct = default)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var result = new List<OrderWithNotifications>(orders.Count);
        foreach (var order in orders)
        {
            var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(order.Id), ct);
            await RefreshStatusesAsync(notifications, ct);
            result.Add(new OrderWithNotifications(order, notifications));
        }
        return result;
    }

    public async Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsAsync(int orderId, string buyerId, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(orderId, ct);
        if (order is null || order.BuyerId != buyerId)
        {
            return null; // not the caller's / does not exist
        }

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(orderId), ct);
        await RefreshStatusesAsync(notifications, ct);
        return notifications;
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct = default)
    {
        // Idempotency: a repeat under the same key returns the notification already produced — no second send.
        var prior = await _notifications.FirstOrDefaultAsync(new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), ct);
        if (prior is not null)
        {
            return ResendResult.Replay(prior.Id);
        }

        var original = await _notifications.GetByIdAsync(notificationId, ct);
        if (original is null)
        {
            return ResendResult.NotFound();
        }
        if (original.ContentDisposed || string.IsNullOrEmpty(original.Body))
        {
            return ResendResult.Unresendable(original.Id, "The message content has been disposed of and cannot be re-sent.");
        }

        var resend = new OrderNotification(original.OrderId, original.BuyerId, original.Kind, original.ToNumber, original.Body);
        resend.MarkAsResendOf(original.Id, idempotencyKey);
        try
        {
            var sent = await _smsProvider.SendAsync(original.ToNumber, original.Body, ct);
            resend.RecordSendResult(sent.Sid, sent.Status);
        }
        catch (SmsProviderException ex)
        {
            resend.RecordSendFailure();
            _logger.LogWarning("Resend of notification {0} could not be sent (recorded as failed): {1}", original.Id, ex.Message);
        }

        resend = await _notifications.AddAsync(resend, ct);
        return ResendResult.Sent(resend.Id);
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken ct = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, ct);
        if (notification is null)
        {
            return false;
        }

        // Disposal is only complete once the provider has redacted the body. If that fails we let it surface
        // and leave the notification un-disposed, so the operation can be retried — we never report success
        // while the text is still retrievable from the provider.
        if (notification.ProviderMessageSid is not null && !notification.ContentDisposed)
        {
            await _smsProvider.RedactContentAsync(notification.ProviderMessageSid, ct);
        }

        notification.DisposeContent();
        await _notifications.UpdateAsync(notification, ct);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        // The provider is asked directly for its record of messages from our sending number over the range.
        var providerMessages = await _smsProvider.ListSentMessagesAsync(from, to, ct);
        var eShopNotifications = await _notifications.ListAsync(new OrderNotificationsSentInRangeSpecification(from, to), ct);

        var eShopBySid = eShopNotifications
            .Where(n => n.ProviderMessageSid is not null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());
        var providerSids = providerMessages
            .Where(m => m.Sid is not null)
            .Select(m => m.Sid!)
            .ToHashSet();

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var eShopOnly = new List<ReconciliationEntry>();

        foreach (var message in providerMessages)
        {
            if (message.Sid is not null && eShopBySid.TryGetValue(message.Sid, out var notification))
            {
                matched.Add(new ReconciliationEntry(
                    message.Sid, message.From, message.Status, message.DateSent, notification.Id, notification.OrderId, notification.DeliveryStatus));
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry(message.Sid, message.From, message.Status, message.DateSent, null, null, null));
            }
        }

        foreach (var notification in eShopNotifications)
        {
            if (notification.ProviderMessageSid is null || !providerSids.Contains(notification.ProviderMessageSid))
            {
                eShopOnly.Add(new ReconciliationEntry(
                    notification.ProviderMessageSid, null, null, null, notification.Id, notification.OrderId, notification.DeliveryStatus));
            }
        }

        return new ReconciliationReport(from, to, _smsProvider.SendingNumber, matched, providerOnly, eShopOnly);
    }

    // ---- helpers -------------------------------------------------------------------------------------

    private async Task NotifyBuyerAsync(int orderId, string buyerId, NotificationKind kind, string body, CancellationToken ct)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
        // A shopper with no number on file is simply not messaged.
        foreach (var number in numbers)
        {
            var notification = new OrderNotification(orderId, buyerId, kind, number.PhoneNumber, body);
            try
            {
                var sent = await _smsProvider.SendAsync(number.PhoneNumber, body, ct);
                notification.RecordSendResult(sent.Sid, sent.Status);
            }
            catch (SmsProviderException ex)
            {
                notification.RecordSendFailure();
                _logger.LogWarning("SMS ({0}) for order {1} could not be sent (recorded as failed): {2}", kind, orderId, ex.Message);
            }
            await _notifications.AddAsync(notification, ct);
        }
    }

    private async Task ScheduleFollowUpAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var body = FollowUpBody(orderId);
        foreach (var number in numbers)
        {
            var notification = new OrderNotification(orderId, buyerId, NotificationKind.DeliveryFollowUp, number.PhoneNumber, body);
            notification.MarkScheduled(sendAt);
            try
            {
                var scheduled = await _smsProvider.ScheduleAsync(number.PhoneNumber, body, sendAt, ct);
                notification.RecordSendResult(scheduled.Sid, scheduled.Status);
            }
            catch (SmsProviderException ex)
            {
                notification.RecordSendFailure();
                _logger.LogWarning("Follow-up for order {0} could not be scheduled (recorded as failed): {1}", orderId, ex.Message);
            }
            await _notifications.AddAsync(notification, ct);
        }
    }

    private async Task CancelScheduledFollowUpsAsync(int orderId, CancellationToken ct)
    {
        var scheduled = await _notifications.ListAsync(new ScheduledFollowUpsByOrderSpecification(orderId), ct);
        foreach (var notification in scheduled)
        {
            try
            {
                await _smsProvider.CancelScheduledAsync(notification.ProviderMessageSid!, ct);
                notification.MarkCanceled();
                await _notifications.UpdateAsync(notification, ct);
            }
            catch (SmsProviderException ex)
            {
                // Best-effort: the provider may report it already sent / not cancelable. Leave the record as-is.
                _logger.LogWarning("Could not cancel scheduled follow-up {0} for order {1}: {2}", notification.Id, orderId, ex.Message);
            }
        }
    }

    private async Task RefreshStatusesAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken ct)
    {
        foreach (var notification in notifications)
        {
            if (notification.ProviderMessageSid is null)
            {
                continue;
            }
            if (notification.DeliveryStatus is not null && TerminalStatuses.Contains(notification.DeliveryStatus))
            {
                continue;
            }

            try
            {
                var status = await _smsProvider.FetchStatusAsync(notification.ProviderMessageSid, ct);
                if (status is not null && status != notification.DeliveryStatus)
                {
                    notification.UpdateDeliveryStatus(status);
                    await _notifications.UpdateAsync(notification, ct);
                }
            }
            catch (SmsProviderException ex)
            {
                _logger.LogWarning("Could not refresh status for notification {0}: {1}", notification.Id, ex.Message);
            }
        }
    }

    private static Address DefaultShipToAddress() =>
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    private static string OrderPlacedBody(Order order) =>
        $"eShop: your order #{order.Id} has been placed. Total: {order.Total().ToString("C", CultureInfo.GetCultureInfo("en-US"))}. Thank you!";

    private static string OrderDispatchedBody(int orderId) =>
        $"eShop: good news — your order #{orderId} is on its way!";

    private static string OrderCancelledBody(int orderId) =>
        $"eShop: your order #{orderId} has been cancelled. If this is unexpected, please contact support.";

    private static string FollowUpBody(int orderId) =>
        $"eShop: how did the delivery of your order #{orderId} go? We'd love your feedback.";
}
