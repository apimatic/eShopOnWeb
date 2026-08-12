using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How far after dispatch the "how did delivery go" follow-up is queued.</summary>
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ISmsProvider _smsProvider;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ISmsProvider smsProvider,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _smsProvider = smsProvider;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> items, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(items, nameof(items));
        if (items.Count == 0)
        {
            throw new EmptyBasketOnCheckoutException();
        }

        var catalogItemIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);

        var orderItems = items.Select(line =>
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new CatalogItemNotFoundException(line.CatalogItemId);
            Guard.Against.NegativeOrZero(line.Quantity, nameof(line.Quantity));

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        // Reuse the app's existing Order/OrderItem model. The API surface carries no
        // shipping address, so a placeholder satisfies the model's required address.
        var shipToAddress = new Address("N/A", "N/A", "N/A", "N/A", "00000");
        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orders.AddAsync(order, cancellationToken);

        var body = $"eShopOnWeb: your order #{order.Id} has been placed. Total {FormatMoney(order.Total())}. Thanks for shopping with us!";
        await NotifyBuyerAsync(order, NotificationType.OrderPlaced, body, cancellationToken);

        return order;
    }

    public async Task DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken)
            ?? throw new OrderNotFoundException(orderId);

        order.Dispatch();
        await _orders.UpdateAsync(order, cancellationToken);

        var numbers = await GetBuyerNumbersAsync(order.BuyerId, cancellationToken);
        var dispatchedBody = $"eShopOnWeb: good news — your order #{order.Id} is on its way!";
        var followUpBody = $"eShopOnWeb: how did the delivery of order #{order.Id} go? Reply with your feedback — we'd love to hear from you.";
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);

        foreach (var number in numbers)
        {
            // Tell the shopper it is on its way.
            await SendImmediateAsync(order, NotificationType.OrderDispatched, number, dispatchedBody, cancellationToken);

            // Queue the follow-up with the PROVIDER for a few days later — not held here.
            await ScheduleFollowUpAsync(order, number, followUpBody, sendAt, cancellationToken);
        }
    }

    public async Task CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken)
            ?? throw new OrderNotFoundException(orderId);

        order.Cancel();
        await _orders.UpdateAsync(order, cancellationToken);

        // A follow-up that has not yet gone out must never reach the customer: call off
        // every still-scheduled follow-up for this order with the provider.
        var pending = await _notifications.ListAsync(new PendingScheduledNotificationsByOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in pending)
        {
            try
            {
                await _smsProvider.CancelScheduledAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.MarkCanceled();
                await _notifications.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to cancel a scheduled follow-up for order {OrderId}: {Error}", orderId, ex.Message);
            }
        }

        var numbers = await GetBuyerNumbersAsync(order.BuyerId, cancellationToken);
        var body = $"eShopOnWeb: your order #{order.Id} has been cancelled. If this is unexpected, please contact support.";
        foreach (var number in numbers)
        {
            await SendImmediateAsync(order, NotificationType.OrderCancelled, number, body, cancellationToken);
        }
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            return ResendResult.NotFound();
        }

        // Idempotency: a repeat under the same key must not send a second message.
        var priorForKey = await _notifications.FirstOrDefaultAsync(
            new ResendByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (priorForKey is not null)
        {
            return ResendResult.Ok(priorForKey);
        }

        // Nothing to resend if the content has been disposed of.
        if (original.ContentRedacted || string.IsNullOrEmpty(original.Body))
        {
            return ResendResult.Disposed();
        }

        var resend = new OrderNotification(original.OrderId, original.OwnerId, original.Type, original.ToNumber, original.Body);
        resend.SetIdempotencyKey(idempotencyKey);
        resend.SetResendOf(original.Id);

        try
        {
            var sent = await _smsProvider.SendAsync(original.ToNumber, original.Body, cancellationToken);
            resend.MarkSent(sent.Sid, sent.Status);
            if (!string.IsNullOrEmpty(sent.ErrorCode) || !string.IsNullOrEmpty(sent.ErrorMessage))
            {
                resend.UpdateDeliveryStatus(sent.Status, sent.ErrorCode, sent.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            resend.MarkSendFailed(ex.Message);
            _logger.LogWarning("Resend of notification {NotificationId} failed to send: {Error}", notificationId, ex.Message);
        }

        resend = await _notifications.AddAsync(resend, cancellationToken);
        return ResendResult.Ok(resend);
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotificationNotFoundException(notificationId);

        if (notification.ContentRedacted)
        {
            return; // already disposed of
        }

        // Redact at the provider first so the text is no longer retrievable there.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            await _smsProvider.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
    {
        // Provider's own record, filtered to this application's sending number at the provider.
        var providerMessages = await _smsProvider.ListSentFromConfiguredNumberAsync(fromUtc, toUtc, cancellationToken);

        // eShop's own record over the same range.
        var eShopNotifications = await _notifications.ListAsync(new SentNotificationsInRangeSpecification(fromUtc, toUtc), cancellationToken);

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First());
        var eShopBySid = eShopNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationEntry>();
        var inProviderOnly = new List<ReconciliationEntry>();
        var inEShopOnly = new List<ReconciliationEntry>();

        foreach (var kvp in providerBySid)
        {
            if (eShopBySid.TryGetValue(kvp.Key, out var notification))
            {
                matched.Add(new ReconciliationEntry(kvp.Key, true, true, kvp.Value.Status, notification.DeliveryStatus, notification.Id, notification.OrderId));
            }
            else
            {
                inProviderOnly.Add(new ReconciliationEntry(kvp.Key, true, false, kvp.Value.Status, null, null, null));
            }
        }

        foreach (var kvp in eShopBySid)
        {
            if (!providerBySid.ContainsKey(kvp.Key))
            {
                var n = kvp.Value;
                inEShopOnly.Add(new ReconciliationEntry(kvp.Key, false, true, null, n.DeliveryStatus, n.Id, n.OrderId));
            }
        }

        return new ReconciliationReport(
            fromUtc, toUtc,
            FromNumber: _smsProvider.FromNumber,
            ProviderCount: providerBySid.Count,
            EShopCount: eShopBySid.Count,
            MatchedCount: matched.Count,
            InProviderNotInEShop: inProviderOnly,
            InEShopNotInProvider: inEShopOnly,
            Matched: matched);
    }

    public async Task RefreshDeliveryStatusesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var current = await _smsProvider.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                if (!string.Equals(current.Status, notification.DeliveryStatus, StringComparison.OrdinalIgnoreCase)
                    || current.ErrorCode != notification.ErrorCode)
                {
                    notification.UpdateDeliveryStatus(current.Status, current.ErrorCode, current.ErrorMessage);
                    await _notifications.UpdateAsync(notification, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to refresh delivery status for a notification: {Error}", ex.Message);
            }
        }
    }

    // -- helpers ---------------------------------------------------------------

    private async Task<IReadOnlyList<ContactNumber>> GetBuyerNumbersAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(buyerId), cancellationToken);
    }

    private async Task NotifyBuyerAsync(Order order, NotificationType type, string body, CancellationToken cancellationToken)
    {
        var numbers = await GetBuyerNumbersAsync(order.BuyerId, cancellationToken);
        // A shopper with no number on file is simply not messaged.
        foreach (var number in numbers)
        {
            await SendImmediateAsync(order, type, number, body, cancellationToken);
        }
    }

    private async Task<OrderNotification> SendImmediateAsync(Order order, NotificationType type, ContactNumber number, string body, CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(order.Id, order.BuyerId, type, number.PhoneNumber, body);
        try
        {
            var sent = await _smsProvider.SendAsync(number.PhoneNumber, body, cancellationToken);
            notification.MarkSent(sent.Sid, sent.Status);
            if (!string.IsNullOrEmpty(sent.ErrorCode) || !string.IsNullOrEmpty(sent.ErrorMessage))
            {
                notification.UpdateDeliveryStatus(sent.Status, sent.ErrorCode, sent.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            // A message that cannot be sent must never fail the underlying operation.
            notification.MarkSendFailed(ex.Message);
            _logger.LogWarning("Failed to send a {Type} message for order {OrderId}: {Error}", type, order.Id, ex.Message);
        }

        return await _notifications.AddAsync(notification, cancellationToken);
    }

    private async Task<OrderNotification> ScheduleFollowUpAsync(Order order, ContactNumber number, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(order.Id, order.BuyerId, NotificationType.DeliveryFollowUp, number.PhoneNumber, body);
        try
        {
            var scheduled = await _smsProvider.ScheduleAsync(number.PhoneNumber, body, sendAt, cancellationToken);
            notification.MarkScheduled(scheduled.Sid, sendAt, scheduled.Status);
        }
        catch (Exception ex)
        {
            notification.MarkSendFailed(ex.Message);
            _logger.LogWarning("Failed to schedule the delivery follow-up for order {OrderId}: {Error}", order.Id, ex.Message);
        }

        return await _notifications.AddAsync(notification, cancellationToken);
    }

    private static string FormatMoney(decimal amount) => amount.ToString("C", CultureInfo.GetCultureInfo("en-US"));
}
