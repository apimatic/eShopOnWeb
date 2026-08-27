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

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<ContactNumber> _contactRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<ResendIdempotencyRecord> _idempotencyRepository;
    private readonly ITwilioMessagingGateway _twilio;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IRepository<ContactNumber> contactRepository,
        IRepository<OrderNotification> notificationRepository,
        IRepository<ResendIdempotencyRecord> idempotencyRepository,
        ITwilioMessagingGateway twilio,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _contactRepository = contactRepository;
        _notificationRepository = notificationRepository;
        _idempotencyRepository = idempotencyRepository;
        _twilio = twilio;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken)
    {
        await SendForOrderAsync(
            order,
            NotificationKind.OrderPlaced,
            $"Your order #{order.Id} has been placed.",
            sendAt: null,
            relatedNotificationId: null,
            cancellationToken);
    }

    public async Task DispatchAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        order.MarkDispatched();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        var dispatched = await SendForOrderAsync(
            order,
            NotificationKind.OrderDispatched,
            $"Your order #{order.Id} is on its way.",
            sendAt: null,
            relatedNotificationId: null,
            cancellationToken);

        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        await SendForOrderAsync(
            order,
            NotificationKind.DispatchFollowUp,
            $"How did the delivery of order #{order.Id} go?",
            sendAt,
            dispatched?.Id,
            cancellationToken);
    }

    public async Task CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        await CancelPendingFollowUpsAsync(orderId, cancellationToken);

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        await SendForOrderAsync(
            order,
            NotificationKind.OrderCancelled,
            $"Your order #{order.Id} has been cancelled.",
            sendAt: null,
            relatedNotificationId: null,
            cancellationToken);
    }

    public async Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        if (orders.Count == 0)
        {
            return orders;
        }

        var notifications = await _notificationRepository.ListAsync(
            new OrderNotificationsByOrderIdsSpecification(orders.Select(o => o.Id)),
            cancellationToken);

        await RefreshAsync(notifications, cancellationToken);
        return orders;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListNotificationsForOrdersAsync(
        IReadOnlyList<int> orderIds,
        CancellationToken cancellationToken)
    {
        if (orderIds.Count == 0)
        {
            return Array.Empty<OrderNotification>();
        }

        return await _notificationRepository.ListAsync(
            new OrderNotificationsByOrderIdsSpecification(orderIds),
            cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListOrderNotificationsAsync(
        int orderId,
        string? buyerId,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (!isAdmin && (buyerId is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal)))
        {
            throw new OrderNotFoundException(orderId);
        }

        var notifications = await _notificationRepository.ListAsync(
            new OrderNotificationsByOrderIdSpecification(orderId),
            cancellationToken);
        await RefreshAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.");
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotificationNotFoundException(notificationId);

        var existing = await _idempotencyRepository.FirstOrDefaultAsync(
            new ResendIdempotencySpecification(notificationId, idempotencyKey.Trim()),
            cancellationToken);
        if (existing is not null)
        {
            var prior = await _notificationRepository.GetByIdAsync(existing.ResultNotificationId, cancellationToken);
            if (prior is not null)
            {
                await RefreshAsync(new[] { prior }, cancellationToken);
                return prior;
            }
        }

        if (!original.CanResend())
        {
            throw new InvalidOperationException("This notification cannot be re-sent.");
        }

        var order = await GetOrderAsync(original.OrderId, cancellationToken);
        var destination = await ResolveDestinationAsync(order.BuyerId, cancellationToken);
        if (destination is null)
        {
            throw new InvalidOperationException("The shopper has no contact number on file.");
        }

        var resend = new OrderNotification(
            order.Id,
            order.BuyerId,
            NotificationKind.Resend,
            destination,
            original.Body!);
        resend.RelateTo(original.Id);
        await _notificationRepository.AddAsync(resend, cancellationToken);

        await AttemptSendAsync(resend, sendAt: null, cancellationToken);

        await _idempotencyRepository.AddAsync(
            new ResendIdempotencyRecord(original.Id, idempotencyKey.Trim(), resend.Id),
            cancellationToken);

        return resend;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotificationNotFoundException(notificationId);

        if (!string.IsNullOrEmpty(notification.ProviderSid))
        {
            MessageSendResult? result = null;
            for (var attempt = 0; attempt < 5; attempt++)
            {
                result = await _twilio.RedactBodyAsync(notification.ProviderSid, cancellationToken);
                if (result.Succeeded)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }

            if (result is null || !result.Succeeded)
            {
                throw new ProviderUnavailableException(
                    result?.FailureReason ?? "The provider could not dispose of the message content.");
            }

            notification.ApplyProviderState(result.Status, result.ErrorCode, result.ErrorMessage);
        }

        notification.RedactLocalContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Redacted content for notification {NotificationId}", notificationId);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (to <= from)
        {
            throw new ArgumentException("The reconciliation window requires 'to' to be after 'from'.");
        }

        var providerPage = await _twilio.ListFromNumberAsync(from, to, cancellationToken);
        var local = await _notificationRepository.ListAsync(
            new NotificationsWithProviderSidInRangeSpecification(from.AddDays(-1), to.AddDays(1)),
            cancellationToken);

        var localBySid = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderSid))
            .GroupBy(n => n.ProviderSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matched = new List<ReconciliationMatch>();
        var providerOnly = new List<ReconciliationProviderEntry>();
        var seenSids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var message in providerPage.Messages)
        {
            seenSids.Add(message.Sid);
            if (localBySid.TryGetValue(message.Sid, out var notification))
            {
                matched.Add(new ReconciliationMatch(notification.Id, message.Sid, message.Status));
            }
            else
            {
                providerOnly.Add(new ReconciliationProviderEntry(message.Sid, message.Status, message.DateSent));
            }
        }

        var localOnly = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderSid) && !seenSids.Contains(n.ProviderSid!))
            .Select(n => new ReconciliationLocalEntry(n.Id, n.ProviderSid, n.ProviderStatus))
            .ToList();

        return new ReconciliationReport(
            from,
            to,
            _twilio.FromNumber,
            matched,
            providerOnly,
            localOnly,
            providerPage.Incomplete);
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var pending = await _notificationRepository.ListAsync(
            new PendingFollowUpByOrderSpecification(orderId),
            cancellationToken);

        foreach (var followUp in pending)
        {
            var result = await _twilio.CancelScheduledAsync(followUp.ProviderSid!, cancellationToken);
            if (result.Succeeded)
            {
                followUp.ApplyProviderState(result.Status, result.ErrorCode, result.ErrorMessage);
            }
            else
            {
                followUp.RecordSendFailure(result.FailureReason ?? "Failed to cancel the scheduled follow-up.");
                _logger.LogWarning(
                    "Could not cancel scheduled follow-up {NotificationId}: {Reason}",
                    followUp.Id,
                result.FailureReason ?? "unknown");
            }

            await _notificationRepository.UpdateAsync(followUp, cancellationToken);
        }
    }

    private async Task<OrderNotification?> SendForOrderAsync(
        Order order,
        NotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        int? relatedNotificationId,
        CancellationToken cancellationToken)
    {
        var destination = await ResolveDestinationAsync(order.BuyerId, cancellationToken);
        if (destination is null)
        {
            _logger.LogInformation("Skipping {Kind} notification for order {OrderId}; no contact number on file.", kind, order.Id);
            return null;
        }

        var notification = new OrderNotification(order.Id, order.BuyerId, kind, destination, body);
        if (sendAt.HasValue)
        {
            notification.MarkScheduled(sendAt.Value);
        }

        if (relatedNotificationId.HasValue)
        {
            notification.RelateTo(relatedNotificationId.Value);
        }

        await _notificationRepository.AddAsync(notification, cancellationToken);
        await AttemptSendAsync(notification, sendAt, cancellationToken);
        return notification;
    }

    private async Task AttemptSendAsync(OrderNotification notification, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _twilio.SendAsync(notification.DestinationNumber, notification.Body ?? string.Empty, sendAt, cancellationToken);
            if (!string.IsNullOrEmpty(result.Sid) || result.Succeeded)
            {
                notification.RecordProviderAccepted(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage);
            }

            if (!result.Succeeded)
            {
                notification.RecordSendFailure(result.FailureReason ?? "The provider did not accept the message.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            notification.RecordSendFailure("The notification could not be sent.");
            _logger.LogWarning("Notification {NotificationId} send failed.", notification.Id);
        }

        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    private async Task RefreshAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderSid))
            {
                continue;
            }

            try
            {
                var fetched = await _twilio.FetchAsync(notification.ProviderSid, cancellationToken);
                if (fetched is null)
                {
                    continue;
                }

                notification.ApplyProviderState(fetched.Status, fetched.ErrorCode, fetched.ErrorMessage);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Could not refresh notification {NotificationId}.", notification.Id);
            }
        }
    }

    private async Task<string?> ResolveDestinationAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers.FirstOrDefault()?.CanonicalNumber;
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderByIdSpecification(orderId), cancellationToken);
        return order ?? throw new OrderNotFoundException(orderId);
    }
}
