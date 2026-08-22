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

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<NotificationResendAttempt> _resendAttempts;
    private readonly ISmsGateway _smsGateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IRepository<NotificationResendAttempt> resendAttempts,
        ISmsGateway smsGateway,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _resendAttempts = resendAttempts;
        _smsGateway = smsGateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        Address shipToAddress,
        IReadOnlyList<OrderLine> lines,
        CancellationToken cancellationToken = default)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new EmptyBasketOnCheckoutException("An order must contain at least one item.");
        }

        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new ArgumentException("Each order line must have a quantity greater than zero.");
        }

        var catalogIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(catalogIds), cancellationToken);
        var missing = catalogIds.Except(catalogItems.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
        {
            throw new CatalogItemNotFoundException(missing);
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orders.AddAsync(order, cancellationToken);

        await TryNotifyAsync(
            order,
            NotificationKind.OrderPlaced,
            $"eShopOnWeb: your order #{order.Id} has been placed. Total {order.Total():0.00}.",
            sendAt: null,
            cancellationToken);

        return order;
    }

    public async Task DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderWithItemsAsync(orderId, cancellationToken);
        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);

        await TryNotifyAsync(
            order,
            NotificationKind.OrderDispatched,
            $"eShopOnWeb: order #{order.Id} is on its way.",
            sendAt: null,
            cancellationToken);

        await TryNotifyAsync(
            order,
            NotificationKind.DeliveryFollowUp,
            $"eShopOnWeb: how did delivery of order #{order.Id} go? Reply to this message with your feedback.",
            sendAt: DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay),
            cancellationToken);
    }

    public async Task CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderWithItemsAsync(orderId, cancellationToken);
        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);

        var pendingFollowUps = await _notifications.ListAsync(new PendingFollowUpsByOrderSpec(order.Id), cancellationToken);
        foreach (var followUp in pendingFollowUps)
        {
            await CancelProviderMessageAsync(followUp, cancellationToken);
        }

        await TryNotifyAsync(
            order,
            NotificationKind.OrderCancelled,
            $"eShopOnWeb: order #{order.Id} has been cancelled.",
            sendAt: null,
            cancellationToken);
    }

    public async Task<ShopperOrdersResult> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var notifications = orders.Count == 0
            ? new List<OrderNotification>()
            : await _notifications.ListAsync(new NotificationsByOrderIdsSpec(orders.Select(o => o.Id)), cancellationToken);
        await RefreshAsync(notifications, cancellationToken);
        return new ShopperOrdersResult(orders, notifications);
    }

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(
        string buyerId,
        int orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new OrderNotFoundException(orderId);
        }

        var notifications = await _notifications.ListAsync(
            new NotificationsByBuyerAndOrderSpec(buyerId, orderId), cancellationToken);
        await RefreshAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(
        int notificationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotificationNotFoundException(notificationId);

        var existingAttempt = await _resendAttempts.FirstOrDefaultAsync(
            new ResendAttemptByKeySpec(notificationId, idempotencyKey.Trim()), cancellationToken);
        if (existingAttempt is not null)
        {
            return await _notifications.GetByIdAsync(existingAttempt.ResultingNotificationId, cancellationToken)
                ?? throw new NotificationNotFoundException(existingAttempt.ResultingNotificationId);
        }

        if (original.ContentRedacted || string.IsNullOrWhiteSpace(original.Body))
        {
            throw new OrderStateException("The original message content has been disposed of and cannot be resent.");
        }

        if (!await DestinationStillRegisteredAsync(original.BuyerId, original.DestinationNumber, cancellationToken))
        {
            throw new OrderStateException("The destination number is no longer on file for this shopper.");
        }

        var resent = await SendAndStoreAsync(
            original.OrderId,
            original.BuyerId,
            original.Kind,
            original.Body,
            original.DestinationNumber,
            sendAt: null,
            cancellationToken);

        var attempt = new NotificationResendAttempt(original.Id, idempotencyKey.Trim(), resent.Id);
        await _resendAttempts.AddAsync(attempt, cancellationToken);
        return resent;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotificationNotFoundException(notificationId);

        if (!string.IsNullOrWhiteSpace(notification.ProviderSid))
        {
            await _smsGateway.RedactBodyAsync(notification.ProviderSid, cancellationToken);
            var snapshot = await _smsGateway.FetchAsync(notification.ProviderSid, cancellationToken);
            if (snapshot is not null)
            {
                notification.ApplyProviderState(snapshot.Sid, snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage);
            }
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The 'to' timestamp must be on or after 'from'.");
        }

        var fromNumber = _smsGateway.ConfiguredFromNumber;
        var local = await _notifications.ListAsync(new NotificationsWithProviderSidsSpec(), cancellationToken);
        await RefreshAsync(local, cancellationToken);
        var providerMessages = await _smsGateway.ListSentFromAsync(fromNumber, from, to, cancellationToken);

        var localBySid = local
            .Where(n => !string.IsNullOrWhiteSpace(n.ProviderSid))
            .GroupBy(n => n.ProviderSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrWhiteSpace(m.Sid))
            .GroupBy(m => m.Sid, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var entries = new List<ReconciliationEntry>();

        foreach (var provider in providerMessages)
        {
            localBySid.TryGetValue(provider.Sid, out var localMatch);
            entries.Add(new ReconciliationEntry(
                provider.Sid,
                localMatch?.Id,
                provider.Status,
                localMatch?.ProviderStatus,
                localMatch is null ? "provider_only" : "matched"));
        }

        foreach (var localNote in local)
        {
            if (string.IsNullOrWhiteSpace(localNote.ProviderSid))
            {
                continue;
            }

            if (providerBySid.ContainsKey(localNote.ProviderSid))
            {
                continue;
            }

            if (!IsInRange(localNote, from, to))
            {
                continue;
            }

            entries.Add(new ReconciliationEntry(
                localNote.ProviderSid,
                localNote.Id,
                ProviderStatus: null,
                localNote.ProviderStatus,
                "eshop_only"));
        }

        return new ReconciliationReport(from, to, fromNumber, entries);
    }

    private static bool IsInRange(OrderNotification notification, DateTimeOffset from, DateTimeOffset to)
    {
        var stamp = notification.CreatedAt;
        return stamp >= from && stamp <= to;
    }

    private async Task<Order> GetOrderWithItemsAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new OrderNotFoundException(orderId);
        }

        return order;
    }

    private async Task TryNotifyAsync(
        Order order,
        NotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        var destination = await GetActiveDestinationAsync(order.BuyerId, cancellationToken);
        if (destination is null)
        {
            return;
        }

        try
        {
            await SendAndStoreAsync(order.Id, order.BuyerId, kind, body, destination, sendAt, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Order {OrderId} notification {Kind} could not be handed to the provider. {ExceptionType}",
                order.Id, kind, ex.GetType().Name);

            var failed = new OrderNotification(
                order.Id,
                order.BuyerId,
                kind,
                body,
                destination,
                providerSid: null,
                providerStatus: "not_sent",
                providerErrorCode: null,
                providerErrorMessage: "Provider rejected or failed the send.",
                scheduledSendAt: sendAt);
            await _notifications.AddAsync(failed, cancellationToken);
        }
    }

    private async Task<OrderNotification> SendAndStoreAsync(
        int orderId,
        string buyerId,
        NotificationKind kind,
        string body,
        string destination,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        var snapshot = await _smsGateway.SendAsync(new SmsSendRequest(destination, body, sendAt), cancellationToken);
        var notification = new OrderNotification(
            orderId,
            buyerId,
            kind,
            body,
            destination,
            snapshot.Sid,
            snapshot.Status,
            snapshot.ErrorCode,
            snapshot.ErrorMessage,
            sendAt);
        return await _notifications.AddAsync(notification, cancellationToken);
    }

    private async Task<string?> GetActiveDestinationAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpec(buyerId), cancellationToken);
        return numbers.FirstOrDefault()?.CanonicalNumber;
    }

    private async Task<bool> DestinationStillRegisteredAsync(string buyerId, string destination, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpec(buyerId), cancellationToken);
        return numbers.Any(n => string.Equals(n.CanonicalNumber, destination, StringComparison.Ordinal));
    }

    private async Task CancelProviderMessageAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(notification.ProviderSid))
        {
            notification.ApplyProviderState(null, "canceled", null, "Cancelled locally; no provider identifier.");
            await _notifications.UpdateAsync(notification, cancellationToken);
            return;
        }

        try
        {
            var snapshot = await _smsGateway.CancelAsync(notification.ProviderSid, cancellationToken);
            if (snapshot is not null)
            {
                notification.ApplyProviderState(snapshot.Sid, snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage);
            }
            else
            {
                notification.ApplyProviderState(notification.ProviderSid, "canceled", null, null);
            }

            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not cancel provider message for notification {NotificationId}. {ExceptionType}",
                notification.Id, ex.GetType().Name);
        }
    }

    private async Task RefreshAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrWhiteSpace(notification.ProviderSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _smsGateway.FetchAsync(notification.ProviderSid, cancellationToken);
                if (snapshot is null)
                {
                    continue;
                }

                notification.ApplyProviderState(snapshot.Sid, snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not refresh provider status for notification {NotificationId}. {ExceptionType}",
                    notification.Id, ex.GetType().Name);
            }
        }
    }
}
