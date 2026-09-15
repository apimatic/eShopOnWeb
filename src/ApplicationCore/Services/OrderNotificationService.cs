using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orders;
    private readonly IRepository<ShopperContactNumber> _contacts;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<ResendIdempotencyRecord> _resendKeys;
    private readonly ITwilioMessagingGateway _messaging;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orders,
        IRepository<ShopperContactNumber> contacts,
        IRepository<OrderNotification> notifications,
        IRepository<ResendIdempotencyRecord> resendKeys,
        ITwilioMessagingGateway messaging,
        IAppLogger<OrderNotificationService> logger)
    {
        _orders = orders;
        _contacts = contacts;
        _notifications = notifications;
        _resendKeys = resendKeys;
        _messaging = messaging;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken)
    {
        await TrySendAsync(
            order,
            OrderNotificationKind.OrderPlaced,
            $"Your eShopOnWeb order #{order.Id} has been placed. Thank you!",
            sendAt: null,
            cancellationToken);
    }

    public async Task DispatchAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await RequireOrderAsync(orderId, cancellationToken);
        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);

        await TrySendAsync(
            order,
            OrderNotificationKind.OrderDispatched,
            $"Your eShopOnWeb order #{order.Id} is on its way.",
            sendAt: null,
            cancellationToken);

        await TrySendAsync(
            order,
            OrderNotificationKind.DeliveryFollowUp,
            $"How did the delivery of your eShopOnWeb order #{order.Id} go?",
            sendAt: DateTimeOffset.UtcNow.Add(FollowUpDelay),
            cancellationToken);
    }

    public async Task CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await RequireOrderAsync(orderId, cancellationToken);
        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);

        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        await TrySendAsync(
            order,
            OrderNotificationKind.OrderCancelled,
            $"Your eShopOnWeb order #{order.Id} has been cancelled.",
            sendAt: null,
            cancellationToken);
    }

    public async Task<IReadOnlyList<ShopperOrderSummary>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var orderIds = orders.Select(o => o.Id).ToList();
        var notifications = orderIds.Count == 0
            ? new List<OrderNotification>()
            : await _notifications.ListAsync(new NotificationsByOrderIdsSpec(orderIds), cancellationToken);

        await SyncAsync(notifications, cancellationToken);

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => (IReadOnlyList<OrderNotification>)g.ToList());
        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => new ShopperOrderSummary(
                o.Id,
                o.Status,
                o.OrderDate,
                o.Total(),
                byOrder.TryGetValue(o.Id, out var list) ? list : Array.Empty<OrderNotification>()))
            .ToList();
    }

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(
        string buyerId,
        int orderId,
        CancellationToken cancellationToken)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            throw new KeyNotFoundException("Order was not found.");
        }

        var notifications = await _notifications.ListAsync(new NotificationsByOrderIdSpec(orderId), cancellationToken);
        await SyncAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(
        int notificationId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        var existing = await _resendKeys.FirstOrDefaultAsync(
            new ResendIdempotencySpec(notificationId, idempotencyKey.Trim()),
            cancellationToken);
        if (existing is not null)
        {
            var prior = await _notifications.GetByIdAsync(existing.ResultNotificationId, cancellationToken);
            if (prior is not null)
            {
                return prior;
            }
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException("Notification was not found.");

        var body = original.ContentRedacted || string.IsNullOrEmpty(original.Body)
            ? FallbackBody(original)
            : original.Body;

        var resent = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            original.Kind,
            body,
            original.DestinationNumber,
            scheduledFor: null,
            resendOfNotificationId: original.Id);

        resent = await _notifications.AddAsync(resent, cancellationToken);

        if (string.IsNullOrEmpty(resent.DestinationNumber))
        {
            resent.MarkSendFailed("No destination is on file for this notification.");
            await _notifications.UpdateAsync(resent, cancellationToken);
        }
        else
        {
            await DeliverAsync(resent, sendAt: null, cancellationToken);
        }

        await _resendKeys.AddAsync(
            new ResendIdempotencyRecord(notificationId, idempotencyKey.Trim(), resent.Id),
            cancellationToken);

        return resent;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException("Notification was not found.");

        if (!string.IsNullOrEmpty(notification.ProviderSid))
        {
            ProviderMessageSnapshot? snapshot = null;
            MessagingProviderException? lastError = null;
            for (var attempt = 1; attempt <= 4; attempt++)
            {
                try
                {
                    snapshot = await _messaging.RedactBodyAsync(notification.ProviderSid, cancellationToken);
                    lastError = null;
                    break;
                }
                catch (MessagingProviderException ex) when (ex.StatusCode == 404 && attempt < 4)
                {
                    lastError = ex;
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
                catch (MessagingProviderException ex)
                {
                    _logger.LogWarning(
                        "Provider content disposal failed for notification {NotificationId}: {Status}",
                        notification.Id,
                        ex.StatusCode?.ToString() ?? "none");
                    throw;
                }
            }

            if (snapshot is null)
            {
                _logger.LogWarning(
                    "Provider content disposal failed for notification {NotificationId}: {Status}",
                    notification.Id,
                    lastError?.StatusCode?.ToString() ?? "none");
                throw lastError ?? new MessagingProviderException("Message content could not be disposed of at the provider.");
            }

            notification.ApplyProviderSnapshot(snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage, snapshot.Body);
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (to <= from)
        {
            throw new ArgumentException("The reconciliation range 'to' must be after 'from'.");
        }

        var (providerMessages, truncated) = await _messaging.ListSentFromConfiguredNumberAsync(from, to, cancellationToken);
        var local = await _notifications.ListAsync(new NotificationsWithProviderSidSpec(), cancellationToken);
        var localBySid = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderSid))
            .GroupBy(n => n.ProviderSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var providerSids = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<ReconciliationEntry>();

        foreach (var message in providerMessages)
        {
            if (string.IsNullOrEmpty(message.Sid))
            {
                continue;
            }

            providerSids.Add(message.Sid);
            localBySid.TryGetValue(message.Sid, out var eshop);
            entries.Add(new ReconciliationEntry(
                message.Sid,
                message.Status,
                message.From,
                message.DateSent,
                eshop?.Id,
                InProvider: true,
                InEshop: eshop is not null));
        }

        foreach (var localItem in local)
        {
            if (string.IsNullOrEmpty(localItem.ProviderSid) || providerSids.Contains(localItem.ProviderSid))
            {
                continue;
            }

            if (localItem.CreatedAt < from || localItem.CreatedAt > to)
            {
                continue;
            }

            entries.Add(new ReconciliationEntry(
                localItem.ProviderSid,
                localItem.Status,
                From: null,
                DateSent: null,
                localItem.Id,
                InProvider: false,
                InEshop: true));
        }

        return new ReconciliationReport(from, to, entries, truncated);
    }

    private async Task TrySendAsync(
        Order order,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        var destination = await LatestDestinationAsync(order.BuyerId, cancellationToken);
        var notification = new OrderNotification(order.Id, order.BuyerId, kind, body, destination, sendAt);
        notification = await _notifications.AddAsync(notification, cancellationToken);

        if (destination is null)
        {
            notification.MarkSendFailed("No contact number on file.");
            await _notifications.UpdateAsync(notification, cancellationToken);
            return;
        }

        await DeliverAsync(notification, sendAt, cancellationToken);
    }

    private async Task DeliverAsync(
        OrderNotification notification,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            ProviderMessageSnapshot snapshot = sendAt is null
                ? await _messaging.SendSmsAsync(notification.DestinationNumber!, notification.Body!, cancellationToken)
                : await _messaging.ScheduleSmsAsync(notification.DestinationNumber!, notification.Body!, sendAt.Value, cancellationToken);

            notification.AttachProviderAccepted(snapshot.Sid, snapshot.Status);
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (MessagingProviderException ex)
        {
            _logger.LogWarning(
                "SMS {Kind} for order {OrderId} notification {NotificationId} did not send (status {Status}).",
                notification.Kind,
                notification.OrderId,
                notification.Id,
                ex.StatusCode?.ToString() ?? "none");
            notification.MarkSendFailed("The provider rejected or could not accept the message.");
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                "SMS {Kind} for order {OrderId} notification {NotificationId} failed unexpectedly.",
                notification.Kind,
                notification.OrderId,
                notification.Id);
            notification.MarkSendFailed("The message could not be sent.");
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var pending = await _notifications.ListAsync(new PendingFollowUpsByOrderSpec(orderId), cancellationToken);
        foreach (var followUp in pending)
        {
            if (string.IsNullOrEmpty(followUp.ProviderSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _messaging.CancelScheduledAsync(followUp.ProviderSid, cancellationToken);
                followUp.MarkCancelledAtProvider(snapshot.Status);
                await _notifications.UpdateAsync(followUp, cancellationToken);
            }
            catch (MessagingProviderException ex)
            {
                _logger.LogWarning(
                    "Could not cancel scheduled follow-up {NotificationId} (status {Status}).",
                    followUp.Id,
                    ex.StatusCode?.ToString() ?? "none");
            }
        }
    }

    private async Task SyncAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _messaging.FetchMessageAsync(notification.ProviderSid, cancellationToken);
                notification.ApplyProviderSnapshot(snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage, snapshot.Body);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (MessagingProviderException ex)
            {
                _logger.LogWarning(
                    "Could not refresh notification {NotificationId} (status {Status}).",
                    notification.Id,
                    ex.StatusCode?.ToString() ?? "none");
            }
        }
    }

    private async Task<string?> LatestDestinationAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contacts.ListAsync(new ContactNumbersByBuyerSpec(buyerId), cancellationToken);
        return numbers.FirstOrDefault()?.CanonicalNumber;
    }

    private async Task<Order> RequireOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        return await _orders.GetByIdAsync(orderId, cancellationToken)
            ?? throw new KeyNotFoundException("Order was not found.");
    }

    private static string FallbackBody(OrderNotification original) => original.Kind switch
    {
        OrderNotificationKind.OrderPlaced => $"Your eShopOnWeb order #{original.OrderId} has been placed. Thank you!",
        OrderNotificationKind.OrderDispatched => $"Your eShopOnWeb order #{original.OrderId} is on its way.",
        OrderNotificationKind.DeliveryFollowUp => $"How did the delivery of your eShopOnWeb order #{original.OrderId} go?",
        OrderNotificationKind.OrderCancelled => $"Your eShopOnWeb order #{original.OrderId} has been cancelled.",
        _ => $"An update about your eShopOnWeb order #{original.OrderId}."
    };
}
