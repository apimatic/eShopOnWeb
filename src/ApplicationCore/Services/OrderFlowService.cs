using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderFlowService : IOrderFlowService
{
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ISmsGateway _smsGateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderFlowService> _logger;

    public OrderFlowService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ISmsGateway smsGateway,
        IUriComposer uriComposer,
        IAppLogger<OrderFlowService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _smsGateway = smsGateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogOrderLine> items, CancellationToken cancellationToken)
    {
        if (items is null || items.Count == 0)
        {
            throw new ArgumentException("At least one catalog item is required.", nameof(items));
        }

        if (items.Any(i => i.Quantity <= 0))
        {
            throw new ArgumentException("Quantities must be greater than zero.", nameof(items));
        }

        var catalogIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(catalogIds), cancellationToken);
        if (catalogItems.Count != catalogIds.Length)
        {
            throw new KeyNotFoundException("One or more catalog items were not found.");
        }

        var orderItems = items.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, DefaultShippingAddress(), orderItems);
        order = await _orders.AddAsync(order, cancellationToken);

        var body = $"Your eShop order #{order.Id} has been placed. Total: {order.Total():0.00}.";
        await TryNotifyAsync(order, OrderNotificationKind.OrderPlaced, body, scheduledAt: null, cancellationToken);
        return order;
    }

    public async Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await RequireOrderAsync(orderId, cancellationToken);
        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);

        var dispatchedBody = $"Your eShop order #{order.Id} is on its way.";
        await TryNotifyAsync(order, OrderNotificationKind.OrderDispatched, dispatchedBody, scheduledAt: null, cancellationToken);

        var followUpAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var followUpBody = $"How did the delivery of eShop order #{order.Id} go? We would love your feedback.";
        await TryNotifyAsync(order, OrderNotificationKind.DeliveryFollowUp, followUpBody, followUpAt, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await RequireOrderAsync(orderId, cancellationToken);
        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);

        await CancelOutstandingFollowUpsAsync(order, cancellationToken);

        var body = $"Your eShop order #{order.Id} has been cancelled.";
        await TryNotifyAsync(order, OrderNotificationKind.OrderCancelled, body, scheduledAt: null, cancellationToken);
        return order;
    }

    public async Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        if (orders.Count == 0)
        {
            return Array.Empty<OrderWithNotifications>();
        }

        var notifications = await _notifications.ListAsync(
            new OrderNotificationsByOrderIdsSpec(orders.Select(o => o.Id)), cancellationToken);
        await RefreshFromProviderAsync(notifications, cancellationToken);

        return orders
            .OrderByDescending(o => o.Id)
            .Select(order => new OrderWithNotifications(
                order,
                notifications.Where(n => n.OrderId == order.Id).ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderByIdSpec(orderId), cancellationToken);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new KeyNotFoundException("Order not found.");
        }

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderSpec(orderId), cancellationToken);
        await RefreshFromProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        var existing = await _notifications.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpec(idempotencyKey.Trim()), cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var original = await _notifications.FirstOrDefaultAsync(new OrderNotificationByIdSpec(notificationId), cancellationToken);
        if (original is null)
        {
            throw new KeyNotFoundException("Notification not found.");
        }

        if (original.ContentDisposed || string.IsNullOrEmpty(original.Body))
        {
            throw new OrderStateException("The original message content is no longer available to resend.");
        }

        var destination = await ResolveSendableDestinationAsync(original.BuyerId, cancellationToken);
        var resend = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            OrderNotificationKind.Resend,
            original.Body,
            destination,
            originalNotificationId: original.Id,
            resendIdempotencyKey: idempotencyKey.Trim());

        if (destination is null)
        {
            resend.RecordSendFailure("skipped", "No mobile number is on file for this shopper.");
            return await _notifications.AddAsync(resend, cancellationToken);
        }

        var send = await _smsGateway.SendImmediateAsync(destination, original.Body, cancellationToken);
        ApplySendResult(resend, send, "resend");
        return await _notifications.AddAsync(resend, cancellationToken);
    }

    public async Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _notifications.FirstOrDefaultAsync(new OrderNotificationByIdSpec(notificationId), cancellationToken);
        if (notification is null)
        {
            throw new KeyNotFoundException("Notification not found.");
        }

        if (!string.IsNullOrWhiteSpace(notification.ProviderSid))
        {
            var redact = await _smsGateway.RedactBodyAsync(notification.ProviderSid, cancellationToken);
            if (redact is GatewayResult.Failed failed)
            {
                throw new SmsProviderException(failed.Message);
            }

            if (redact is GatewayResult.Unknown unknown)
            {
                throw new SmsProviderException(unknown.Message);
            }
        }

        notification.DisposeContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (to < from)
        {
            throw new ArgumentException("The end of the range must be on or after the start.");
        }

        var providerList = await _smsGateway.ListSentFromConfiguredNumberAsync(from, to, cancellationToken);
        var providerMessages = providerList.Messages;
        var truncated = providerList.Truncated;
        var local = await _notifications.ListAsync(new OrderNotificationsInRangeSpec(from, to), cancellationToken);

        var localBySid = local
            .Where(n => !string.IsNullOrWhiteSpace(n.ProviderSid))
            .GroupBy(n => n.ProviderSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matched = new List<ReconciliationRow>();
        var providerOnly = new List<ReconciliationRow>();
        var seenSids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var message in providerMessages)
        {
            if (string.IsNullOrWhiteSpace(message.Sid))
            {
                providerOnly.Add(ToProviderRow(message, null));
                continue;
            }

            seenSids.Add(message.Sid);
            if (localBySid.TryGetValue(message.Sid, out var notification))
            {
                matched.Add(ToProviderRow(message, notification.Id));
            }
            else
            {
                providerOnly.Add(ToProviderRow(message, null));
            }
        }

        var eshopOnly = local
            .Where(n => string.IsNullOrWhiteSpace(n.ProviderSid) || !seenSids.Contains(n.ProviderSid))
            .Select(n => new ReconciliationRow(n.Id, n.ProviderSid, n.DeliveryStatus, n.ContentDisposed ? null : n.Body, n.ProviderDateSent, "eshop"))
            .ToList();

        return new ReconciliationReport(from, to, _smsGateway.FromNumber, truncated, matched, providerOnly, eshopOnly);
    }

    private async Task TryNotifyAsync(
        Order order,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset? scheduledAt,
        CancellationToken cancellationToken)
    {
        var destination = await ResolveSendableDestinationAsync(order.BuyerId, cancellationToken);
        var notification = new OrderNotification(order.Id, order.BuyerId, kind, body, destination, scheduledAt);

        if (destination is null)
        {
            notification.RecordSendFailure("skipped", "No mobile number is on file for this shopper.");
            await _notifications.AddAsync(notification, cancellationToken);
            return;
        }

        GatewayResult result;
        try
        {
            result = scheduledAt.HasValue
                ? await _smsGateway.ScheduleAsync(destination, body, scheduledAt.Value, cancellationToken)
                : await _smsGateway.SendImmediateAsync(destination, body, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("SMS notification {Kind} for order {OrderId} failed without failing the order.", kind, order.Id);
            notification.RecordSendFailure("unknown", "The messaging provider could not be reached.");
            await _notifications.AddAsync(notification, cancellationToken);
            return;
        }

        ApplySendResult(notification, result, kind.ToString());
        await _notifications.AddAsync(notification, cancellationToken);
    }

    private void ApplySendResult(OrderNotification notification, GatewayResult result, string kind)
    {
        switch (result)
        {
            case GatewayResult.Ok ok:
                notification.RecordAccepted(ok.Message.Sid, ok.Message.Status, ok.Message.ErrorCode, ok.Message.ErrorMessage, ok.Message.DateSent);
                break;
            case GatewayResult.Failed failed:
                _logger.LogWarning("SMS notification {Kind} for order {OrderId} was rejected by the provider.", kind, notification.OrderId);
                notification.RecordSendFailure("failed", failed.Message);
                break;
            case GatewayResult.Unknown unknown:
                _logger.LogWarning("SMS notification {Kind} for order {OrderId} ended with an unknown provider outcome.", kind, notification.OrderId);
                notification.RecordSendFailure("unknown", unknown.Message);
                break;
        }
    }

    private async Task CancelOutstandingFollowUpsAsync(Order order, CancellationToken cancellationToken)
    {
        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderSpec(order.Id), cancellationToken);
        var followUps = notifications.Where(n =>
            n.Kind == OrderNotificationKind.DeliveryFollowUp
            && !string.IsNullOrWhiteSpace(n.ProviderSid)
            && !IsTerminal(n.DeliveryStatus)).ToList();

        foreach (var followUp in followUps)
        {
            try
            {
                var cancel = await _smsGateway.CancelScheduledAsync(followUp.ProviderSid!, cancellationToken);
                switch (cancel)
                {
                    case GatewayResult.Ok ok:
                        followUp.ApplyProviderState(ok.Message.Status, ok.Message.ErrorCode, ok.Message.ErrorMessage, ok.Message.DateSent, ok.Message.Body);
                        break;
                    case GatewayResult.Failed:
                    case GatewayResult.Unknown:
                        var fetch = await _smsGateway.FetchAsync(followUp.ProviderSid!, cancellationToken);
                        if (fetch is GatewayResult.Ok fetched)
                        {
                            followUp.ApplyProviderState(fetched.Message.Status, fetched.Message.ErrorCode, fetched.Message.ErrorMessage, fetched.Message.DateSent, fetched.Message.Body);
                        }
                        break;
                }

                await _notifications.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Cancelling a scheduled follow-up for order {OrderId} failed; the order cancel still succeeded.", order.Id);
            }
        }
    }

    private async Task RefreshFromProviderAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrWhiteSpace(notification.ProviderSid) || IsTerminal(notification.DeliveryStatus))
            {
                continue;
            }

            try
            {
                var fetch = await _smsGateway.FetchAsync(notification.ProviderSid, cancellationToken);
                if (fetch is GatewayResult.Ok ok)
                {
                    notification.ApplyProviderState(ok.Message.Status, ok.Message.ErrorCode, ok.Message.ErrorMessage, ok.Message.DateSent, notification.ContentDisposed ? null : ok.Message.Body);
                    await _notifications.UpdateAsync(notification, cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Refreshing provider status for notification {NotificationId} failed.", notification.Id);
            }
        }
    }

    private async Task<string?> ResolveSendableDestinationAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpec(buyerId), cancellationToken);
        return numbers.FirstOrDefault()?.PhoneNumber;
    }

    private async Task<Order> RequireOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new KeyNotFoundException("Order not found.");
        }

        return order;
    }

    private static Address DefaultShippingAddress() =>
        new("123 Main Street", "Seattle", "WA", "USA", "98101");

    private static bool IsTerminal(string status) =>
        status is "delivered" or "undelivered" or "failed" or "canceled" or "read" or "skipped";

    private static ReconciliationRow ToProviderRow(ProviderMessage message, int? notificationId) =>
        new(notificationId, message.Sid, message.Status, message.Body, message.DateSent, "provider");
}
