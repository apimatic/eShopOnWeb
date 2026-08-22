using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ShopperOrderService : IShopperOrderService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IOrderSmsGateway _sms;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<ShopperOrderService> _logger;

    public ShopperOrderService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IOrderSmsGateway sms,
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

    public async Task<Result<Order>> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CatalogQuantity> items,
        Address? shipTo,
        CancellationToken cancellationToken = default)
    {
        if (items == null || items.Count == 0)
        {
            return ResultHelpers.Invalid<Order>("items", "At least one catalog item is required.");
        }

        if (items.Any(i => i.CatalogItemId <= 0 || i.Quantity <= 0))
        {
            return ResultHelpers.Invalid<Order>("items", "Each item must include a catalog item id and a positive quantity.");
        }

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        if (catalogItems.Count != ids.Length)
        {
            return ResultHelpers.Invalid<Order>("items", "One or more catalog items were not found.");
        }

        var orderItems = items.Select(requested =>
        {
            var catalogItem = catalogItems.First(c => c.Id == requested.CatalogItemId);
            var picture = string.IsNullOrEmpty(catalogItem.PictureUri)
                ? "placeholder"
                : _uriComposer.ComposePicUri(catalogItem.PictureUri);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, picture);
            return new OrderItem(itemOrdered, catalogItem.Price, requested.Quantity);
        }).ToList();

        var address = shipTo ?? new Address("123 Main", "Seattle", "WA", "USA", "98101");
        var order = new Order(buyerId, address, orderItems);
        await _orders.AddAsync(order, cancellationToken);

        await TryNotifyAsync(
            order,
            OrderNotificationKind.OrderPlaced,
            $"Your eShopOnWeb order #{order.Id} has been placed. Thank you!",
            scheduleAt: null,
            cancellationToken);

        return Result<Order>.Success(order);
    }

    public async Task<Result<Order>> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order == null)
        {
            return Result<Order>.NotFound();
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            return ResultHelpers.Invalid<Order>("orderId", "A cancelled order cannot be dispatched.");
        }

        if (order.Status == OrderStatus.Dispatched)
        {
            return Result<Order>.Success(order);
        }

        try
        {
            order.MarkDispatched();
        }
        catch (InvalidOperationException)
        {
            return ResultHelpers.Invalid<Order>("orderId", "A cancelled order cannot be dispatched.");
        }

        await _orders.UpdateAsync(order, cancellationToken);

        await TryNotifyAsync(
            order,
            OrderNotificationKind.OrderDispatched,
            $"Your eShopOnWeb order #{order.Id} is on its way.",
            scheduleAt: null,
            cancellationToken);

        await TryNotifyAsync(
            order,
            OrderNotificationKind.DeliveryFollowUp,
            $"How did the delivery of your eShopOnWeb order #{order.Id} go?",
            scheduleAt: DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay),
            cancellationToken);

        return Result<Order>.Success(order);
    }

    public async Task<Result<Order>> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order == null)
        {
            return Result<Order>.NotFound();
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            return Result<Order>.Success(order);
        }

        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);

        await CancelOutstandingFollowUpsAsync(order.Id, cancellationToken);

        await TryNotifyAsync(
            order,
            OrderNotificationKind.OrderCancelled,
            $"Your eShopOnWeb order #{order.Id} has been cancelled.",
            scheduleAt: null,
            cancellationToken);

        return Result<Order>.Success(order);
    }

    public async Task<IReadOnlyList<ShopperOrderSummary>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var notifications = await _notifications.ListAsync(new OrderNotificationsByBuyerSpec(buyerId), cancellationToken);
        await RefreshProviderStateAsync(notifications, cancellationToken);

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => (IReadOnlyList<OrderNotification>)g.ToList());

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => new ShopperOrderSummary(
                o.Id,
                o.Status.ToString(),
                o.OrderDate,
                o.Total(),
                byOrder.TryGetValue(o.Id, out var n) ? n : Array.Empty<OrderNotification>()))
            .ToList();
    }

    public async Task<Result<IReadOnlyList<OrderNotification>>> GetOrderNotificationsAsync(
        string buyerId,
        int orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order == null || order.BuyerId != buyerId)
        {
            return Result<IReadOnlyList<OrderNotification>>.NotFound();
        }

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderIdSpec(orderId), cancellationToken);
        await RefreshProviderStateAsync(notifications, cancellationToken);
        return Result<IReadOnlyList<OrderNotification>>.Success(notifications);
    }

    public async Task<Result<OrderNotification>> ResendAsync(
        int notificationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return ResultHelpers.Invalid<OrderNotification>("idempotencyKey", "An idempotency key is required.");
        }

        var existing = await _notifications.FirstOrDefaultAsync(
            new ResendBySourceAndKeySpec(notificationId, idempotencyKey.Trim()),
            cancellationToken);
        if (existing != null)
        {
            return Result<OrderNotification>.Success(existing);
        }

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (source == null)
        {
            return Result<OrderNotification>.NotFound();
        }

        if (source.ContentRedacted)
        {
            return ResultHelpers.Invalid<OrderNotification>("notificationId", "The content of this message has been disposed of and cannot be resent.");
        }

        var stillRegistered = await IsDestinationStillRegisteredAsync(source.BuyerId, source.DestinationNumber, cancellationToken);
        if (!stillRegistered)
        {
            return ResultHelpers.Invalid<OrderNotification>("notificationId", "The destination is no longer on file for this shopper.");
        }

        var body = source.Body;
        if (string.IsNullOrEmpty(body) && !string.IsNullOrEmpty(source.ProviderMessageSid))
        {
            var fetched = await SafeFetchAsync(source.ProviderMessageSid, cancellationToken);
            body = fetched?.Body ?? string.Empty;
        }

        var sent = await SafeSendAsync(source.DestinationNumber, body, cancellationToken);
        var notification = new OrderNotification(
            source.OrderId,
            source.BuyerId,
            source.Kind,
            body,
            source.DestinationNumber,
            sent?.Sid,
            sent?.Status ?? "send_failed",
            sent?.ErrorCode,
            scheduledSendAt: null,
            sourceNotificationId: source.Id,
            resendIdempotencyKey: idempotencyKey.Trim());

        await _notifications.AddAsync(notification, cancellationToken);
        return Result<OrderNotification>.Success(notification);
    }

    public async Task<Result> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification == null)
        {
            return Result.NotFound();
        }

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            try
            {
                var redacted = await _sms.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
                if (redacted != null)
                {
                    notification.ApplyProviderState(redacted.Status, redacted.ErrorCode, redacted.Body);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to redact provider message {Sid}: {Message}", notification.ProviderMessageSid, ex.Message);
                return Result.Error("The provider could not dispose of the message content.");
            }
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
        return Result.Success();
    }

    public async Task<Result<ReconciliationReport>> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            return ResultHelpers.Invalid<ReconciliationReport>("to", "The 'to' timestamp must be on or after 'from'.");
        }

        IReadOnlyList<ProviderSmsMessage> providerMessages;
        try
        {
            providerMessages = await _sms.ListFromConfiguredSenderAsync(from, to, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Provider reconciliation listing failed: {Message}", ex.Message);
            return Result<ReconciliationReport>.Error("The provider message list could not be retrieved.");
        }

        var local = await _notifications.ListAsync(new OrderNotificationsInCreatedRangeSpec(from, to), cancellationToken);

        var localBySid = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var entries = new List<ReconciliationEntry>();

        foreach (var provider in providerMessages)
        {
            localBySid.TryGetValue(provider.Sid, out var match);
            entries.Add(new ReconciliationEntry(
                provider.Sid,
                provider.Status,
                provider.DateSent ?? provider.DateCreated,
                match?.Id,
                match == null ? "providerOnly" : "matched"));
        }

        foreach (var notification in local)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid) ||
                !providerBySid.ContainsKey(notification.ProviderMessageSid))
            {
                entries.Add(new ReconciliationEntry(
                    notification.ProviderMessageSid,
                    notification.ProviderStatus,
                    notification.CreatedAt,
                    notification.Id,
                    "localOnly"));
            }
        }

        return Result<ReconciliationReport>.Success(new ReconciliationReport(
            from,
            to,
            _sms.ConfiguredFromNumber,
            entries));
    }

    private async Task TryNotifyAsync(
        Order order,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset? scheduleAt,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ContactNumber> destinations;
        try
        {
            destinations = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpec(order.BuyerId), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to load contact numbers for notification kind {Kind}: {Message}", kind, ex.Message);
            return;
        }

        if (destinations.Count == 0)
        {
            return;
        }

        foreach (var destination in destinations)
        {
            try
            {
                ProviderSmsMessage? sent;
                if (scheduleAt.HasValue)
                {
                    sent = await _sms.ScheduleAsync(destination.CanonicalNumber, body, scheduleAt.Value, cancellationToken);
                }
                else
                {
                    sent = await _sms.SendAsync(destination.CanonicalNumber, body, cancellationToken);
                }

                var notification = new OrderNotification(
                    order.Id,
                    order.BuyerId,
                    kind,
                    body,
                    destination.CanonicalNumber,
                    sent.Sid,
                    sent.Status,
                    sent.ErrorCode,
                    scheduleAt);

                await _notifications.AddAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to send {Kind} notification for order {OrderId}: {Message}", kind, order.Id, ex.Message);
            }
        }
    }

    private async Task CancelOutstandingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notifications.ListAsync(new ScheduledFollowUpsByOrderSpec(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            if (string.IsNullOrEmpty(followUp.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var cancelled = await _sms.CancelScheduledAsync(followUp.ProviderMessageSid, cancellationToken);
                if (cancelled != null)
                {
                    followUp.ApplyProviderState(cancelled.Status, cancelled.ErrorCode, cancelled.Body);
                    await _notifications.UpdateAsync(followUp, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to cancel scheduled follow-up {Sid} for order {OrderId}: {Message}",
                    followUp.ProviderMessageSid, orderId, ex.Message);
            }
        }
    }

    private async Task RefreshProviderStateAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            var fetched = await SafeFetchAsync(notification.ProviderMessageSid, cancellationToken);
            if (fetched == null)
            {
                continue;
            }

            var body = notification.ContentRedacted ? string.Empty : fetched.Body;
            notification.ApplyProviderState(fetched.Status, fetched.ErrorCode, body);
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
    }

    private async Task<ProviderSmsMessage?> SafeFetchAsync(string sid, CancellationToken cancellationToken)
    {
        try
        {
            return await _sms.FetchAsync(sid, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to fetch provider message {Sid}: {Message}", sid, ex.Message);
            return null;
        }
    }

    private async Task<ProviderSmsMessage?> SafeSendAsync(string to, string body, CancellationToken cancellationToken)
    {
        try
        {
            return await _sms.SendAsync(to, body, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to resend notification: {Message}", ex.Message);
            return null;
        }
    }

    private async Task<bool> IsDestinationStillRegisteredAsync(string buyerId, string destinationNumber, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpec(buyerId), cancellationToken);
        return numbers.Any(n => string.Equals(n.CanonicalNumber, destinationNumber, StringComparison.Ordinal));
    }
}
