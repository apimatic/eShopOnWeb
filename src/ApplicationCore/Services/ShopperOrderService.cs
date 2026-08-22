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

public class ShopperOrderService : IShopperOrderService
{
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(4);

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IContactNumberService _contactNumbers;
    private readonly ISmsNotificationGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<ShopperOrderService> _logger;

    public ShopperOrderService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<OrderNotification> notifications,
        IContactNumberService contactNumbers,
        ISmsNotificationGateway gateway,
        IUriComposer uriComposer,
        IAppLogger<ShopperOrderService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceAsync(
        string buyerId,
        IReadOnlyList<CatalogOrderLine> lines,
        Address? shipToAddress,
        CancellationToken cancellationToken)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new OrderStateException("An order must contain at least one catalog item.");
        }

        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new OrderStateException("Each catalog item must have a quantity greater than zero.");
            }
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        if (catalogItems.Count != ids.Length)
        {
            throw new OrderStateException("One or more catalog items were not found.");
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

        var address = shipToAddress ?? new Address("123 Main St.", "Kent", "OH", "United States", "44240");
        var order = new Order(buyerId, address, orderItems);
        await _orders.AddAsync(order, cancellationToken);

        await TryNotifyAsync(
            order,
            NotificationKind.OrderPlaced,
            $"Your eShopOnWeb order #{order.Id} has been placed. Total: {order.Total().ToString("C", CultureInfo.GetCultureInfo("en-US"))}.",
            scheduledFor: null,
            cancellationToken);

        return order;
    }

    public async Task DispatchAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken)
            ?? throw new OrderNotFoundException(orderId);

        try
        {
            order.MarkDispatched();
        }
        catch (InvalidOperationException ex)
        {
            throw new OrderStateException(ex.Message);
        }

        await _orders.UpdateAsync(order, cancellationToken);

        await TryNotifyAsync(
            order,
            NotificationKind.OrderDispatched,
            $"Your eShopOnWeb order #{order.Id} is on its way.",
            scheduledFor: null,
            cancellationToken);

        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        await TryNotifyAsync(
            order,
            NotificationKind.DeliveryFollowUp,
            $"How did the delivery of eShopOnWeb order #{order.Id} go?",
            sendAt,
            cancellationToken);
    }

    public async Task CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken)
            ?? throw new OrderNotFoundException(orderId);

        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);

        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        await TryNotifyAsync(
            order,
            NotificationKind.OrderCancelled,
            $"Your eShopOnWeb order #{order.Id} has been cancelled.",
            scheduledFor: null,
            cancellationToken);
    }

    public async Task<IReadOnlyList<ShopperOrderView>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var notifications = await _notifications.ListAsync(new NotificationsByBuyerSpecification(buyerId), cancellationToken);
        await RefreshAsync(notifications, cancellationToken);

        return orders
            .OrderByDescending(o => o.Id)
            .Select(order => ToView(order, notifications.Where(n => n.OrderId == order.Id).ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<NotificationView>> ListNotificationsAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            throw new OrderNotFoundException(orderId);
        }

        var notifications = await _notifications.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshAsync(notifications, cancellationToken);
        return notifications.Select(ToView).ToList();
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new OrderStateException("An idempotency key is required.");
        }

        var existingReplay = await _notifications.FirstOrDefaultAsync(
            new ResendByIdempotencyKeySpecification(notificationId, idempotencyKey),
            cancellationToken);
        if (existingReplay is not null)
        {
            return existingReplay;
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotificationNotFoundException(notificationId);

        if (!string.IsNullOrWhiteSpace(original.ProviderSid))
        {
            try
            {
                await RefreshOneAsync(original, cancellationToken);
            }
            catch (SmsProviderException)
            {
                // Continue with the last stored outcome.
            }
        }

        if (original.HasReachedShopper())
        {
            throw new OrderStateException("This message already reached the shopper.");
        }

        if (!original.ContactNumberId.HasValue)
        {
            throw new OrderStateException("The destination number is no longer on file for this shopper.");
        }

        var destination = await _contactNumbers.GetByIdForBuyerAsync(
            original.BuyerId,
            original.ContactNumberId.Value,
            cancellationToken);
        if (destination is null)
        {
            throw new OrderStateException("The destination number is no longer on file for this shopper.");
        }

        var body = original.ContentRedacted || string.IsNullOrWhiteSpace(original.Body)
            ? BodyFor(original.Kind, original.OrderId)
            : original.Body!;

        var resend = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            original.Kind,
            destination.Id,
            destination.CanonicalNumber,
            body);
        resend.MarkAsResend(original.Id, idempotencyKey);
        await _notifications.AddAsync(resend, cancellationToken);

        await TrySendExistingAsync(resend, scheduledFor: null, cancellationToken);
        return resend;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotificationNotFoundException(notificationId);

        if (!string.IsNullOrWhiteSpace(notification.ProviderSid))
        {
            try
            {
                var redacted = await _gateway.RedactBodyAsync(notification.ProviderSid, cancellationToken);
                notification.ApplyProviderResult(redacted.Sid, redacted.Status, redacted.ErrorCode, redacted.ErrorMessage);
            }
            catch (SmsProviderException ex)
            {
                _logger.LogWarning("Failed to redact provider body for notification {NotificationId}: {Status}", notification.Id, ex.StatusCode?.ToString() ?? "none");
                throw;
            }
        }

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (to <= from)
        {
            throw new OrderStateException("The reconciliation window requires 'to' to be after 'from'.");
        }

        var providerMessages = await _gateway.ListSentFromConfiguredNumberAsync(from, to, cancellationToken);
        var eshop = await _notifications.ListAsync(new NotificationsWithProviderSidSpecification(), cancellationToken);

        var bySid = eshop
            .Where(n => !string.IsNullOrWhiteSpace(n.ProviderSid))
            .GroupBy(n => n.ProviderSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var providerSids = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<ReconciliationMessageRow>();

        foreach (var message in providerMessages)
        {
            if (string.IsNullOrWhiteSpace(message.Sid))
            {
                continue;
            }

            providerSids.Add(message.Sid);
            if (bySid.TryGetValue(message.Sid, out var local))
            {
                rows.Add(new ReconciliationMessageRow(message.Sid, message.Status, message.DateSent, local.Id, "matched"));
            }
            else
            {
                rows.Add(new ReconciliationMessageRow(message.Sid, message.Status, message.DateSent, null, "providerOnly"));
            }
        }

        var eshopOnly = eshop.Where(n =>
            !string.IsNullOrWhiteSpace(n.ProviderSid)
            && !providerSids.Contains(n.ProviderSid)
            && CreatedInRange(n, from, to)).ToList();

        foreach (var local in eshopOnly)
        {
            rows.Add(new ReconciliationMessageRow(local.ProviderSid, local.ProviderStatus, null, local.Id, "eshopOnly"));
        }

        var matched = rows.Count(r => r.Alignment == "matched");
        var providerOnly = rows.Count(r => r.Alignment == "providerOnly");
        var eshopOnlyCount = rows.Count(r => r.Alignment == "eshopOnly");

        return new ReconciliationReport(
            from,
            to,
            _gateway.ConfiguredFromNumber,
            providerSids.Count,
            matched + eshopOnlyCount,
            matched,
            providerOnly,
            eshopOnlyCount,
            false,
            rows);
    }

    private static bool CreatedInRange(OrderNotification notification, DateTimeOffset from, DateTimeOffset to)
    {
        return notification.CreatedAt >= from && notification.CreatedAt <= to;
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notifications.ListAsync(new FollowUpByOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            if (string.IsNullOrWhiteSpace(followUp.ProviderSid))
            {
                continue;
            }

            try
            {
                var updated = await _gateway.CancelScheduledAsync(followUp.ProviderSid, cancellationToken);
                followUp.ApplyProviderResult(updated.Sid, updated.Status, updated.ErrorCode, updated.ErrorMessage);
                await _notifications.UpdateAsync(followUp, cancellationToken);
                _logger.LogInformation("Cancelled scheduled follow-up {NotificationId}.", followUp.Id);
            }
            catch (SmsProviderException ex)
            {
                _logger.LogWarning("Could not cancel scheduled follow-up {NotificationId}: {Status}", followUp.Id, ex.StatusCode?.ToString() ?? "none");
                try
                {
                    await RefreshOneAsync(followUp, cancellationToken);
                }
                catch (SmsProviderException)
                {
                    // Best-effort: the order is already cancelled.
                }
            }
        }
    }

    private async Task TryNotifyAsync(
        Order order,
        NotificationKind kind,
        string body,
        DateTimeOffset? scheduledFor,
        CancellationToken cancellationToken)
    {
        var destination = await _contactNumbers.GetPrimaryAsync(order.BuyerId, cancellationToken);
        if (destination is null)
        {
            _logger.LogInformation("Skipping {Kind} SMS for order {OrderId}; no number on file.", kind, order.Id);
            return;
        }

        var notification = new OrderNotification(order.Id, order.BuyerId, kind, destination.Id, destination.CanonicalNumber, body);
        if (scheduledFor.HasValue)
        {
            notification.MarkScheduled(scheduledFor.Value);
        }

        await _notifications.AddAsync(notification, cancellationToken);
        await TrySendExistingAsync(notification, scheduledFor, cancellationToken);
    }

    private async Task TrySendExistingAsync(
        OrderNotification notification,
        DateTimeOffset? scheduledFor,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(notification.DestinationE164))
        {
            notification.MarkSendFailed("No destination on file.");
            await _notifications.UpdateAsync(notification, cancellationToken);
            return;
        }

        try
        {
            ProviderMessageSnapshot snapshot;
            if (scheduledFor.HasValue)
            {
                snapshot = await _gateway.ScheduleAsync(
                    notification.DestinationE164,
                    notification.Body ?? string.Empty,
                    scheduledFor.Value,
                    cancellationToken);
            }
            else
            {
                snapshot = await _gateway.SendImmediatelyAsync(
                    notification.DestinationE164,
                    notification.Body ?? string.Empty,
                    cancellationToken);
            }

            notification.ApplyProviderResult(snapshot.Sid, snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage);
            await _notifications.UpdateAsync(notification, cancellationToken);
            _logger.LogInformation("Recorded provider message for notification {NotificationId} with status {Status}.", notification.Id, snapshot.Status ?? "none");
        }
        catch (SmsProviderException ex)
        {
            notification.MarkSendFailed("The messaging provider did not accept the message.");
            await _notifications.UpdateAsync(notification, cancellationToken);
            _logger.LogWarning("SMS send failed for notification {NotificationId}: {Status}", notification.Id, ex.StatusCode?.ToString() ?? "none");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            notification.MarkSendFailed("The messaging provider did not accept the message.");
            await _notifications.UpdateAsync(notification, cancellationToken);
            _logger.LogWarning("SMS send failed for notification {NotificationId}.", notification.Id);
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
                await RefreshOneAsync(notification, cancellationToken);
            }
            catch (SmsProviderException ex)
            {
                _logger.LogWarning("Could not refresh notification {NotificationId}: {Status}", notification.Id, ex.StatusCode?.ToString() ?? "none");
            }
        }
    }

    private async Task RefreshOneAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(notification.ProviderSid))
        {
            return;
        }

        var snapshot = await _gateway.FetchAsync(notification.ProviderSid, cancellationToken);
        notification.ApplyProviderResult(snapshot.Sid, snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage);
        if (notification.ContentRedacted)
        {
            notification.RedactContent();
        }
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    private static string BodyFor(NotificationKind kind, int orderId) => kind switch
    {
        NotificationKind.OrderPlaced => $"Your eShopOnWeb order #{orderId} has been placed.",
        NotificationKind.OrderDispatched => $"Your eShopOnWeb order #{orderId} is on its way.",
        NotificationKind.DeliveryFollowUp => $"How did the delivery of eShopOnWeb order #{orderId} go?",
        NotificationKind.OrderCancelled => $"Your eShopOnWeb order #{orderId} has been cancelled.",
        _ => $"Update for eShopOnWeb order #{orderId}."
    };

    private static ShopperOrderView ToView(Order order, IReadOnlyList<OrderNotification> notifications)
    {
        return new ShopperOrderView(
            order.Id,
            order.Status.ToString(),
            order.OrderDate,
            order.Total(),
            order.OrderItems.Select(i => new ShopperOrderItemView(
                i.ItemOrdered.CatalogItemId,
                i.ItemOrdered.ProductName,
                i.UnitPrice,
                i.Units)).ToList(),
            notifications.Select(ToView).ToList());
    }

    private static NotificationView ToView(OrderNotification n)
    {
        return new NotificationView(
            n.Id,
            n.Kind,
            n.ContentRedacted ? null : n.Body,
            n.ProviderSid,
            n.ProviderStatus,
            n.ProviderErrorCode,
            n.ProviderErrorMessage,
            n.ContentRedacted,
            n.CreatedAt,
            n.ScheduledFor,
            n.ResentFromNotificationId,
            n.SendFailure);
    }
}
