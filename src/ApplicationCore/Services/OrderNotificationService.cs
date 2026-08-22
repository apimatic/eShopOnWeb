using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IContactNumberService _contactNumberService;
    private readonly ISmsNotificationGateway _smsGateway;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IRepository<OrderNotification> notificationRepository,
        IContactNumberService contactNumberService,
        ISmsNotificationGateway smsGateway,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _notificationRepository = notificationRepository;
        _contactNumberService = contactNumberService;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken)
    {
        await TrySendAsync(
            order,
            NotificationKind.OrderPlaced,
            $"eShopOnWeb: Your order #{order.Id} has been placed. Thank you!",
            scheduleAt: null,
            cancellationToken);
    }

    public async Task DispatchAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        order.MarkDispatched();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        await TrySendAsync(
            order,
            NotificationKind.OrderDispatched,
            $"eShopOnWeb: Your order #{order.Id} is on its way.",
            scheduleAt: null,
            cancellationToken);

        await TrySendAsync(
            order,
            NotificationKind.DeliveryFollowUp,
            $"eShopOnWeb: How did the delivery of order #{order.Id} go?",
            scheduleAt: DateTimeOffset.UtcNow.Add(FollowUpDelay),
            cancellationToken);
    }

    public async Task CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        await CancelOutstandingFollowUpsAsync(order, cancellationToken);

        await TrySendAsync(
            order,
            NotificationKind.OrderCancelled,
            $"eShopOnWeb: Your order #{order.Id} has been cancelled.",
            scheduleAt: null,
            cancellationToken);
    }

    public async Task<IReadOnlyList<ShopperOrderSummary>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        if (orders.Count == 0)
        {
            return Array.Empty<ShopperOrderSummary>();
        }

        var notifications = await _notificationRepository.ListAsync(
            new NotificationsByOrderIdsSpec(orders.Select(o => o.Id)),
            cancellationToken);

        await RefreshFromProviderAsync(notifications, cancellationToken);

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => (IReadOnlyList<OrderNotification>)g.ToList());
        return orders
            .Select(order => new ShopperOrderSummary(
                order,
                byOrder.TryGetValue(order.Id, out var list) ? list : Array.Empty<OrderNotification>()))
            .ToList();
    }

    public async Task<IReadOnlyList<OrderNotification>> GetNotificationsForOrderAsync(
        int orderId,
        string buyerId,
        bool isAdministrator,
        CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (!isAdministrator && order.BuyerId != buyerId)
        {
            throw new OrderNotFoundException(orderId);
        }

        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpec(orderId), cancellationToken);
        await RefreshFromProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new NotificationNotEligibleException("An idempotency key is required.");
        }

        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new NotificationByParentAndIdempotencySpec(notificationId, idempotencyKey.Trim()),
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotificationNotFoundException(notificationId);

        await RefreshFromProviderAsync(new[] { original }, cancellationToken);

        if (!original.DidNotReachShopper())
        {
            throw new NotificationNotEligibleException("Only messages that did not reach the shopper can be re-sent.");
        }

        var order = await GetOrderAsync(original.OrderId, cancellationToken);
        var body = original.BodyRedacted || string.IsNullOrEmpty(original.Body)
            ? ComposeBody(original.Kind, order.Id)
            : original.Body;

        var destination = await _contactNumberService.GetLatestForBuyerAsync(order.BuyerId, cancellationToken);
        var notification = new OrderNotification(
            order.Id,
            order.BuyerId,
            original.Kind,
            body,
            destination?.Id,
            scheduledFor: null,
            parentNotificationId: original.Id,
            idempotencyKey: idempotencyKey.Trim());

        if (destination is null)
        {
            notification.RecordSendResult(null, "send_failed", null, "No contact number on file.");
            await _notificationRepository.AddAsync(notification, cancellationToken);
            return notification;
        }

        await DeliverAsync(notification, destination.CanonicalNumber, scheduleAt: null, cancellationToken);
        await _notificationRepository.AddAsync(notification, cancellationToken);
        return notification;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotificationNotFoundException(notificationId);

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            try
            {
                var snapshot = await _smsGateway.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
                if (snapshot is not null)
                {
                    notification.ApplyProviderSnapshot(snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage, snapshot.Body);
                }
            }
            catch (Exception ex) when (ex is SmsProviderException or OperationCanceledException)
            {
                _logger.LogWarning("Failed to redact provider content for notification {NotificationId}.", notificationId);
                throw;
            }
        }

        notification.MarkBodyRedacted();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (to < from)
        {
            throw new NotificationNotEligibleException("The reconciliation 'to' value must be on or after 'from'.");
        }

        var providerPage = await _smsGateway.ListSentFromConfiguredNumberAsync(from, to, cancellationToken);
        var providerMessages = providerPage.Messages;

        var local = await _notificationRepository.ListAsync(
            new NotificationsInCreatedRangeWithSidSpec(from.AddDays(-1), to.AddDays(1)),
            cancellationToken);

        var localBySid = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var providerSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();

        foreach (var message in providerMessages)
        {
            providerSids.Add(message.Sid);
            if (localBySid.TryGetValue(message.Sid, out var notification))
            {
                matched.Add(new ReconciliationEntry(
                    message.Sid,
                    notification.Id,
                    notification.DeliveryStatus,
                    message.Status,
                    message.DateSent));
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry(
                    message.Sid,
                    null,
                    null,
                    message.Status,
                    message.DateSent));
            }
        }

        var localOnly = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid) && !providerSids.Contains(n.ProviderMessageSid!))
            .Select(n => new ReconciliationEntry(n.ProviderMessageSid, n.Id, n.DeliveryStatus, null, null))
            .ToList();

        return new ReconciliationReport(from, to, matched, providerOnly, localOnly, providerPage.Truncated);
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        return order ?? throw new OrderNotFoundException(orderId);
    }

    private async Task TrySendAsync(
        Order order,
        NotificationKind kind,
        string body,
        DateTimeOffset? scheduleAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var destination = await _contactNumberService.GetLatestForBuyerAsync(order.BuyerId, cancellationToken);
            if (destination is null)
            {
                _logger.LogInformation("Skipping {Kind} notification for order {OrderId}; no contact number on file.", kind, order.Id);
                return;
            }

            var notification = new OrderNotification(
                order.Id,
                order.BuyerId,
                kind,
                body,
                destination.Id,
                scheduleAt);

            await DeliverAsync(notification, destination.CanonicalNumber, scheduleAt, cancellationToken);
            await _notificationRepository.AddAsync(notification, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Notification {Kind} for order {OrderId} failed and will not fail the order operation.", kind, order.Id);
        }
    }

    private async Task DeliverAsync(
        OrderNotification notification,
        string canonicalNumber,
        DateTimeOffset? scheduleAt,
        CancellationToken cancellationToken)
    {
        SmsSendResult result;
        try
        {
            result = scheduleAt.HasValue
                ? await _smsGateway.ScheduleAsync(canonicalNumber, notification.Body!, scheduleAt.Value, cancellationToken)
                : await _smsGateway.SendImmediateAsync(canonicalNumber, notification.Body!, cancellationToken);
        }
        catch (Exception ex) when (ex is SmsProviderException or OperationCanceledException or System.Text.Json.JsonException)
        {
            notification.RecordSendResult(null, "send_failed", (ex as SmsProviderException)?.StatusCode, "Provider send failed.");
            return;
        }

        notification.RecordSendResult(result.ProviderSid, result.Status, result.ErrorCode, result.ErrorMessage);
    }

    private async Task CancelOutstandingFollowUpsAsync(Order order, CancellationToken cancellationToken)
    {
        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpec(order.Id), cancellationToken);
        foreach (var followUp in notifications.Where(n => n.IsScheduledFollowUp()))
        {
            try
            {
                var result = await _smsGateway.CancelScheduledAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.RecordSendResult(
                    followUp.ProviderMessageSid,
                    result.Status ?? "canceled",
                    result.ErrorCode,
                    result.ErrorMessage);
                await _notificationRepository.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    "Failed to cancel scheduled follow-up notification {NotificationId} for order {OrderId}.",
                    followUp.Id,
                    order.Id);
            }
        }
    }

    private async Task RefreshFromProviderAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            if (notification.IsTerminal())
            {
                continue;
            }

            try
            {
                var snapshot = await _smsGateway.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                if (snapshot is null)
                {
                    continue;
                }

                notification.ApplyProviderSnapshot(snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage, snapshot.Body);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Failed to refresh provider status for notification {NotificationId}.", notification.Id);
            }
        }
    }

    private static string ComposeBody(NotificationKind kind, int orderId) => kind switch
    {
        NotificationKind.OrderPlaced => $"eShopOnWeb: Your order #{orderId} has been placed. Thank you!",
        NotificationKind.OrderDispatched => $"eShopOnWeb: Your order #{orderId} is on its way.",
        NotificationKind.DeliveryFollowUp => $"eShopOnWeb: How did the delivery of order #{orderId} go?",
        NotificationKind.OrderCancelled => $"eShopOnWeb: Your order #{orderId} has been cancelled.",
        _ => $"eShopOnWeb: An update about order #{orderId}."
    };
}
