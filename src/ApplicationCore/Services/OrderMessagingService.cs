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

public class OrderMessagingService : IOrderMessagingService
{
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ISmsGateway _smsGateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderMessagingService> _logger;

    public OrderMessagingService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ShopperContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ISmsGateway smsGateway,
        IUriComposer uriComposer,
        IAppLogger<OrderMessagingService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _smsGateway = smsGateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<PlaceOrderLine> lines,
        Address shipTo,
        CancellationToken cancellationToken)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new EmptyBasketOnCheckoutException("An order must contain at least one item.");
        }

        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(lines), "Quantity must be greater than zero.");
            }
        }

        var catalogIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(catalogIds), cancellationToken);
        foreach (var id in catalogIds)
        {
            if (catalogItems.All(c => c.Id != id))
            {
                throw new CatalogItemNotFoundException(id);
            }
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

        var order = new Order(buyerId, shipTo, orderItems);
        order = await _orders.AddAsync(order, cancellationToken);

        await TryNotifyAsync(
            order,
            OrderNotificationKind.OrderPlaced,
            $"Your eShop order #{order.Id} has been placed. Thank you for shopping with us.",
            scheduleAt: null,
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
        catch (InvalidOperationException)
        {
            throw;
        }

        await _orders.UpdateAsync(order, cancellationToken);

        await TryNotifyAsync(
            order,
            OrderNotificationKind.OrderDispatched,
            $"Your eShop order #{order.Id} is on its way.",
            scheduleAt: null,
            cancellationToken);

        await TryNotifyAsync(
            order,
            OrderNotificationKind.DeliveryFollowUp,
            $"How did the delivery of eShop order #{order.Id} go? We would love your feedback.",
            scheduleAt: DateTimeOffset.UtcNow.Add(FollowUpDelay),
            cancellationToken);
    }

    public async Task CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken)
                    ?? throw new OrderNotFoundException(orderId);

        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);

        var followUps = await _notifications.ListAsync(
            new ScheduledFollowUpsByOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            if (string.IsNullOrEmpty(followUp.ProviderSid))
            {
                continue;
            }

            await RefreshNotificationAsync(followUp, cancellationToken);
            if (!followUp.IsCancellableSchedule())
            {
                continue;
            }

            var cancel = await _smsGateway.CancelScheduledAsync(followUp.ProviderSid, cancellationToken);
            if (cancel.Accepted || !string.IsNullOrEmpty(cancel.Status))
            {
                followUp.ApplyProviderSnapshot(cancel.Status ?? "canceled", cancel.ErrorCode, cancel.ErrorMessage);
            }
            else
            {
                followUp.ApplyProviderSnapshot(followUp.ProviderStatus, cancel.ErrorCode, cancel.ErrorMessage);
                _logger.LogWarning("Could not cancel scheduled follow-up {NotificationId}.", followUp.Id);
            }

            await _notifications.UpdateAsync(followUp, cancellationToken);
        }

        await TryNotifyAsync(
            order,
            OrderNotificationKind.OrderCancelled,
            $"Your eShop order #{order.Id} has been cancelled.",
            scheduleAt: null,
            cancellationToken);
    }

    public async Task<IReadOnlyList<ShopperOrderView>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersSpecification(buyerId), cancellationToken);
        if (orders.Count == 0)
        {
            return Array.Empty<ShopperOrderView>();
        }

        var notifications = await _notifications.ListAsync(
            new OrderNotificationsByOrderIdsSpecification(orders.Select(o => o.Id)), cancellationToken);
        foreach (var notification in notifications)
        {
            await RefreshNotificationAsync(notification, cancellationToken);
        }

        return orders
            .OrderByDescending(o => o.Id)
            .Select(order => new ShopperOrderView(
                order.Id,
                order.Status.ToString(),
                order.Total(),
                order.OrderDate,
                notifications.Where(n => n.OrderId == order.Id).Select(ToView).ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<OrderNotificationView>> ListNotificationsAsync(
        int orderId,
        string callerBuyerId,
        bool isAdministrator,
        CancellationToken cancellationToken)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken)
                    ?? throw new OrderNotFoundException(orderId);

        if (!isAdministrator && order.BuyerId != callerBuyerId)
        {
            throw new OrderNotFoundException(orderId);
        }

        var notifications = await _notifications.ListAsync(
            new OrderNotificationsByOrderIdSpecification(orderId), cancellationToken);
        foreach (var notification in notifications)
        {
            await RefreshNotificationAsync(notification, cancellationToken);
        }

        return notifications.Select(ToView).ToList();
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        var existing = await _notifications.FirstOrDefaultAsync(
            new ResendIdempotencySpecification(notificationId, idempotencyKey), cancellationToken);
        if (existing != null)
        {
            await RefreshNotificationAsync(existing, cancellationToken);
            return existing;
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken)
                       ?? throw new NotificationNotFoundException(notificationId);

        await RefreshNotificationAsync(original, cancellationToken);

        if (!original.CanResend())
        {
            throw new InvalidOperationException("This notification cannot be re-sent.");
        }

        var body = original.Body
                   ?? throw new InvalidOperationException("This notification cannot be re-sent.");

        var destination = await ResolveLiveDestinationAsync(original, cancellationToken);
        if (destination is null)
        {
            throw new InvalidOperationException("The shopper has no reachable number on file.");
        }

        var resend = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            original.Kind,
            body,
            destination.CanonicalNumber,
            destination.Id,
            resendOfNotificationId: original.Id,
            idempotencyKey: idempotencyKey);

        resend = await _notifications.AddAsync(resend, cancellationToken);
        await ApplySendAsync(resend, scheduleAt: null, cancellationToken);
        return resend;
    }

    public async Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
                           ?? throw new NotificationNotFoundException(notificationId);

        if (!string.IsNullOrEmpty(notification.ProviderSid))
        {
            var redact = await _smsGateway.RedactBodyAsync(notification.ProviderSid, cancellationToken);
            if (!redact.Accepted && redact.OutcomeUnknown == false && string.IsNullOrEmpty(redact.ProviderSid) && redact.ErrorCode is null)
            {
                _logger.LogWarning("Provider content disposal did not confirm for notification {NotificationId}.", notification.Id);
            }
            else
            {
                notification.ApplyProviderSnapshot(redact.Status ?? notification.ProviderStatus, redact.ErrorCode, redact.ErrorMessage);
            }
        }

        notification.MarkContentDisposed();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<IReadOnlyList<ReconciliationRow>> ReconcileAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        var providerMessages = await _smsGateway.ListSentFromConfiguredNumberAsync(fromUtc, toUtc, cancellationToken);
        var local = await _notifications.ListAsync(
            new NotificationsWithProviderSidInRangeSpecification(fromUtc.AddDays(-1), toUtc.AddDays(1)),
            cancellationToken);

        var localBySid = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderSid))
            .GroupBy(n => n.ProviderSid!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var rows = new List<ReconciliationRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var message in providerMessages)
        {
            seen.Add(message.Sid);
            if (localBySid.TryGetValue(message.Sid, out var notification))
            {
                rows.Add(new ReconciliationRow(
                    message.Sid,
                    "matched",
                    notification.ProviderStatus,
                    message.Status,
                    notification.Id,
                    message.DateSent));
            }
            else
            {
                rows.Add(new ReconciliationRow(
                    message.Sid,
                    "providerOnly",
                    null,
                    message.Status,
                    null,
                    message.DateSent));
            }
        }

        foreach (var notification in local.Where(n => !string.IsNullOrEmpty(n.ProviderSid)))
        {
            if (seen.Contains(notification.ProviderSid!))
            {
                continue;
            }

            if (notification.CreatedUtc < fromUtc || notification.CreatedUtc > toUtc)
            {
                continue;
            }

            rows.Add(new ReconciliationRow(
                notification.ProviderSid!,
                "eShopOnly",
                notification.ProviderStatus,
                null,
                notification.Id,
                null));
        }

        return rows;
    }

    private async Task TryNotifyAsync(
        Order order,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset? scheduleAt,
        CancellationToken cancellationToken)
    {
        var destination = await GetLatestNumberAsync(order.BuyerId, cancellationToken);
        if (destination is null)
        {
            return;
        }

        var notification = new OrderNotification(
            order.Id,
            order.BuyerId,
            kind,
            body,
            destination.CanonicalNumber,
            destination.Id,
            scheduleAt);

        notification = await _notifications.AddAsync(notification, cancellationToken);
        await ApplySendAsync(notification, scheduleAt, cancellationToken);
    }

    private async Task ApplySendAsync(
        OrderNotification notification,
        DateTimeOffset? scheduleAt,
        CancellationToken cancellationToken)
    {
        SmsSendAttempt attempt;
        try
        {
            attempt = scheduleAt.HasValue
                ? await _smsGateway.ScheduleAsync(notification.DestinationNumber, notification.Body!, scheduleAt.Value, cancellationToken)
                : await _smsGateway.SendImmediateAsync(notification.DestinationNumber, notification.Body!, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            notification.RecordSendFailure("The messaging provider could not be reached.");
            await _notifications.UpdateAsync(notification, cancellationToken);
            _logger.LogWarning("Notification {NotificationId} was not accepted by the provider.", notification.Id);
            return;
        }

        if (attempt.Accepted)
        {
            notification.RecordProviderAcceptance(attempt.ProviderSid, attempt.Status, attempt.ErrorCode, attempt.ErrorMessage);
        }
        else if (attempt.OutcomeUnknown)
        {
            notification.RecordProviderAcceptance(attempt.ProviderSid, attempt.Status ?? "unknown", attempt.ErrorCode, attempt.ErrorMessage);
        }
        else
        {
            notification.RecordSendFailure(attempt.ErrorMessage ?? "The messaging provider rejected the message.");
            if (!string.IsNullOrEmpty(attempt.ProviderSid))
            {
                notification.RecordProviderAcceptance(attempt.ProviderSid, attempt.Status, attempt.ErrorCode, attempt.ErrorMessage);
            }
        }

        await _notifications.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation(
            "Recorded notification {NotificationId} with provider status {Status}.",
            notification.Id,
            notification.ProviderStatus ?? "none");
    }

    private async Task RefreshNotificationAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(notification.ProviderSid))
        {
            return;
        }

        try
        {
            var snapshot = await _smsGateway.FetchAsync(notification.ProviderSid, cancellationToken);
            if (snapshot is null)
            {
                return;
            }

            notification.ApplyProviderSnapshot(snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage);
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Could not refresh notification {NotificationId} from the provider.", notification.Id);
        }
    }

    private async Task<ShopperContactNumber?> GetLatestNumberAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers.FirstOrDefault();
    }

    private async Task<ShopperContactNumber?> ResolveLiveDestinationAsync(
        OrderNotification original,
        CancellationToken cancellationToken)
    {
        if (original.ContactNumberId is int contactId)
        {
            var existing = await _contactNumbers.GetByIdAsync(contactId, cancellationToken);
            if (existing != null && existing.BuyerId == original.BuyerId)
            {
                return existing;
            }
        }

        return await GetLatestNumberAsync(original.BuyerId, cancellationToken);
    }

    private static OrderNotificationView ToView(OrderNotification notification) =>
        new(
            notification.Id,
            notification.OrderId,
            notification.Kind,
            notification.Body,
            notification.ContentDisposed,
            notification.ProviderSid,
            notification.ProviderStatus,
            notification.ErrorCode,
            notification.ErrorMessage,
            notification.CreatedUtc,
            notification.ScheduledForUtc,
            notification.ResendOfNotificationId);
}
