using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ShopperOrderService : IShopperOrderService
{
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ShopperContactNumber> _contacts;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IMessagingGateway _messaging;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<ShopperOrderService> _logger;

    public ShopperOrderService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ShopperContactNumber> contacts,
        IRepository<OrderNotification> notifications,
        IMessagingGateway messaging,
        IUriComposer uriComposer,
        IAppLogger<ShopperOrderService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contacts = contacts;
        _notifications = notifications;
        _messaging = messaging;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<PlaceOrderLine> lines,
        PlaceOrderAddress shipTo,
        CancellationToken ct)
    {
        var items = await BuildOrderItemsAsync(lines, ct);
        var address = new Address(shipTo.Street, shipTo.City, shipTo.State, shipTo.Country, shipTo.ZipCode);
        var order = new Order(buyerId, address, items);
        await _orders.AddAsync(order, ct);

        await TryNotifyAsync(
            order,
            NotificationKind.OrderPlaced,
            $"Your eShop order #{order.Id} has been placed. Thank you for your purchase.",
            schedule: false,
            ct);

        return order;
    }

    public async Task DispatchAsync(int orderId, CancellationToken ct)
    {
        var order = await GetOrderOrThrow(orderId, ct);
        try
        {
            order.MarkDispatched();
        }
        catch (InvalidOperationException ex)
        {
            throw new OrderNotificationException(ex.Message, 409);
        }

        await _orders.UpdateAsync(order, ct);

        await TryNotifyAsync(
            order,
            NotificationKind.OrderDispatched,
            $"Your eShop order #{order.Id} is on its way.",
            schedule: false,
            ct);

        await TryNotifyAsync(
            order,
            NotificationKind.DeliveryFollowUp,
            $"How did the delivery of eShop order #{order.Id} go? Reply with any feedback.",
            schedule: true,
            ct);
    }

    public async Task CancelAsync(int orderId, CancellationToken ct)
    {
        var order = await GetOrderOrThrow(orderId, ct);
        try
        {
            order.MarkCancelled();
        }
        catch (InvalidOperationException ex)
        {
            throw new OrderNotificationException(ex.Message, 409);
        }

        await _orders.UpdateAsync(order, ct);

        var existing = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(order.Id), ct);
        foreach (var followUp in existing.Where(n => n.IsPendingFollowUp() && n.ProviderSid is not null))
        {
            try
            {
                var cancelled = await _messaging.CancelScheduledAsync(followUp.ProviderSid!, ct);
                if (cancelled is not null)
                {
                    followUp.ApplyProviderState(
                        cancelled.Status,
                        cancelled.ErrorCode,
                        cancelled.ErrorMessage,
                        ParseProviderTime(cancelled.DateCreated),
                        ParseProviderTime(cancelled.DateSent));
                    await _notifications.UpdateAsync(followUp, ct);
                    _logger.LogInformation("Cancelled scheduled follow-up notification {NotificationId} sid {Sid}", followUp.Id, followUp.ProviderSid ?? "none");
                }
            }
            catch (Exception)
            {
                _logger.LogWarning("Could not cancel scheduled follow-up notification {NotificationId}; order cancel still succeeded.", followUp.Id);
            }
        }

        await TryNotifyAsync(
            order,
            NotificationKind.OrderCancelled,
            $"Your eShop order #{order.Id} has been cancelled.",
            schedule: false,
            ct);
    }

    public async Task<IReadOnlyList<OrderWithNotificationsView>> ListMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersSpecification(buyerId), ct);
        var notifications = await _notifications.ListAsync(new OrderNotificationsByBuyerSpecification(buyerId), ct);
        await RefreshProviderStateAsync(notifications, ct);

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());
        return orders.Select(order =>
        {
            byOrder.TryGetValue(order.Id, out var notes);
            return new OrderWithNotificationsView(
                order.Id,
                order.Status.ToString(),
                order.OrderDate,
                order.Total(),
                (notes ?? new List<OrderNotification>()).Select(ToView).ToList());
        }).ToList();
    }

    public async Task<IReadOnlyList<NotificationView>> ListOrderNotificationsAsync(
        int orderId,
        string buyerId,
        bool isAdmin,
        CancellationToken ct)
    {
        var order = await GetOrderOrThrow(orderId, ct);
        if (!isAdmin && !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new OrderNotificationException("Order not found.", 404);
        }

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(orderId), ct);
        await RefreshProviderStateAsync(notifications, ct);
        return notifications.Select(ToView).ToList();
    }

    public async Task<int> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new OrderNotificationException("An idempotency key is required.");
        }

        var original = await _notifications.GetByIdAsync(notificationId, ct)
                       ?? throw new OrderNotificationException("Notification not found.", 404);

        var existing = await _notifications.FirstOrDefaultAsync(
            new ResendByIdempotencyKeySpecification(notificationId, idempotencyKey), ct);
        if (existing is not null)
        {
            return existing.Id;
        }

        if (original.ContentRedacted || string.IsNullOrEmpty(original.Body))
        {
            throw new OrderNotificationException("Message content is no longer available to resend.", 409);
        }

        if (string.IsNullOrEmpty(original.DestinationNumber))
        {
            throw new OrderNotificationException("This notification has no destination on file.", 409);
        }

        var resend = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            NotificationKind.Resend,
            original.DestinationNumber,
            original.Body);
        resend.MarkResend(original.Id, idempotencyKey);
        await _notifications.AddAsync(resend, ct);

        await DeliverAsync(resend, schedule: false, ct);
        return resend.Id;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken ct)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, ct)
                           ?? throw new OrderNotificationException("Notification not found.", 404);

        if (!string.IsNullOrEmpty(notification.ProviderSid))
        {
            var updated = await _messaging.RedactBodyAsync(notification.ProviderSid, ct);
            if (updated is not null)
            {
                notification.ApplyProviderState(
                    updated.Status,
                    updated.ErrorCode,
                    updated.ErrorMessage,
                    ParseProviderTime(updated.DateCreated),
                    ParseProviderTime(updated.DateSent));
            }
        }

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, ct);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var providerMessages = await _messaging.ListSentFromConfiguredNumberAsync(from, to, ct);
        var local = await _notifications.ListAsync(new NotificationsWithProviderSidSpecification(), ct);
        var localBySid = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderSid))
            .GroupBy(n => n.ProviderSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var providerSids = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<ReconciliationRow>();

        foreach (var message in providerMessages.Messages)
        {
            if (string.IsNullOrEmpty(message.Sid))
            {
                continue;
            }

            providerSids.Add(message.Sid);
            if (localBySid.TryGetValue(message.Sid, out var localNote))
            {
                rows.Add(new ReconciliationRow(
                    message.Sid,
                    localNote.Id.ToString(CultureInfo.InvariantCulture),
                    "matched",
                    message.Status,
                    localNote.ProviderStatus,
                    message.DateSent));
            }
            else
            {
                rows.Add(new ReconciliationRow(
                    message.Sid,
                    null,
                    "provider_only",
                    message.Status,
                    null,
                    message.DateSent));
            }
        }

        foreach (var localNote in local)
        {
            if (string.IsNullOrEmpty(localNote.ProviderSid) || providerSids.Contains(localNote.ProviderSid))
            {
                continue;
            }

            var created = localNote.ProviderDateSent ?? localNote.ProviderDateCreated ?? localNote.CreatedAt;
            if (created < from || created > to)
            {
                continue;
            }

            rows.Add(new ReconciliationRow(
                localNote.ProviderSid,
                localNote.Id.ToString(CultureInfo.InvariantCulture),
                "eshop_only",
                null,
                localNote.ProviderStatus,
                localNote.ProviderDateSent?.ToString("O")));
        }

        return new ReconciliationReport(from, to, _messaging.ConfiguredFromNumber, providerMessages.Truncated, rows);
    }

    private async Task<List<OrderItem>> BuildOrderItemsAsync(IReadOnlyList<PlaceOrderLine> lines, CancellationToken ct)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new OrderNotificationException("At least one catalog item is required.");
        }

        foreach (var line in lines)
        {
            if (line.CatalogItemId <= 0 || line.Quantity <= 0)
            {
                throw new OrderNotificationException("Each line requires a catalogItemId and a quantity greater than zero.");
            }
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), ct);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (!byId.TryGetValue(line.CatalogItemId, out var catalogItem))
            {
                throw new OrderNotificationException($"Catalog item {line.CatalogItemId} was not found.", 404);
            }

            var snapshot = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(snapshot, catalogItem.Price, line.Quantity));
        }

        return items;
    }

    private async Task<Order> GetOrderOrThrow(int orderId, CancellationToken ct)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderByIdSpecification(orderId), ct);
        if (order is null)
        {
            throw new OrderNotificationException("Order not found.", 404);
        }

        return order;
    }

    private async Task TryNotifyAsync(
        Order order,
        NotificationKind kind,
        string body,
        bool schedule,
        CancellationToken ct)
    {
        var destination = await GetLatestNumberAsync(order.BuyerId, ct);
        var notification = new OrderNotification(order.Id, order.BuyerId, kind, destination, body);
        await _notifications.AddAsync(notification, ct);

        if (destination is null)
        {
            notification.RecordSendFailure("skipped_no_number", null, null);
            await _notifications.UpdateAsync(notification, ct);
            _logger.LogInformation("Skipped SMS for notification {NotificationId} because the shopper has no number on file.", notification.Id);
            return;
        }

        await DeliverAsync(notification, schedule, ct);
    }

    private async Task DeliverAsync(OrderNotification notification, bool schedule, CancellationToken ct)
    {
        try
        {
            var sendAt = schedule ? DateTimeOffset.UtcNow.Add(FollowUpDelay) : (DateTimeOffset?)null;
            var result = await _messaging.SendAsync(
                new SendMessageRequest(notification.DestinationNumber!, notification.Body ?? string.Empty, schedule, sendAt),
                ct);

            if (!string.IsNullOrEmpty(result.Sid))
            {
                notification.RecordProviderAcceptance(
                    result.Sid,
                    result.Status,
                    result.ErrorCode,
                    result.ErrorMessage,
                    ParseProviderTime(result.DateCreated),
                    ParseProviderTime(result.DateSent));
            }
            else
            {
                notification.RecordSendFailure(result.Status, result.ErrorCode, result.ErrorMessage);
            }
        }
        catch (Exception)
        {
            notification.RecordSendFailure("send_failed", null, "The provider could not be reached or returned an unreadable response.");
            _logger.LogWarning("SMS send failed for notification {NotificationId}; the order operation still succeeded.", notification.Id);
        }

        await _notifications.UpdateAsync(notification, ct);
        _logger.LogInformation(
            "Recorded notification {NotificationId} kind {Kind} sid {Sid} status {Status}",
            notification.Id,
            notification.Kind.ToString(),
            notification.ProviderSid ?? "none",
            notification.ProviderStatus);
    }

    private async Task<string?> GetLatestNumberAsync(string buyerId, CancellationToken ct)
    {
        var numbers = await _contacts.ListAsync(new ShopperContactNumbersSpecification(buyerId), ct);
        return numbers.FirstOrDefault()?.CanonicalNumber;
    }

    private async Task RefreshProviderStateAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken ct)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _messaging.FetchAsync(notification.ProviderSid, ct);
                if (snapshot is null)
                {
                    continue;
                }

                notification.ApplyProviderState(
                    snapshot.Status,
                    snapshot.ErrorCode,
                    snapshot.ErrorMessage,
                    ParseProviderTime(snapshot.DateCreated),
                    ParseProviderTime(snapshot.DateSent));
                if (notification.ContentRedacted)
                {
                    notification.RedactContent();
                }
                await _notifications.UpdateAsync(notification, ct);
            }
            catch (Exception)
            {
                _logger.LogWarning("Could not refresh provider status for notification {NotificationId}.", notification.Id);
            }
        }
    }

    private static NotificationView ToView(OrderNotification n) =>
        new(
            n.Id,
            n.OrderId,
            n.Kind.ToString(),
            n.ProviderStatus,
            n.ProviderSid,
            n.ProviderErrorCode,
            n.ProviderErrorMessage,
            n.ContentRedacted ? null : n.Body,
            n.ContentRedacted,
            n.CreatedAt);

    private static DateTimeOffset? ParseProviderTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
