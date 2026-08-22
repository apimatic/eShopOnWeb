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
    internal static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered",
        "undelivered",
        "failed",
        "canceled"
    };
    private static readonly HashSet<string> ReachedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered",
        "sent",
        "read"
    };

    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<NotificationResendAttempt> _resendAttempts;
    private readonly IRepository<Order> _orders;
    private readonly ISmsNotificationGateway _gateway;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<ShopperContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IRepository<NotificationResendAttempt> resendAttempts,
        IRepository<Order> orders,
        ISmsNotificationGateway gateway,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _resendAttempts = resendAttempts;
        _orders = orders;
        _gateway = gateway;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken)
        => TrySendAsync(
            order,
            OrderNotificationKind.OrderPlaced,
            $"Your eShop order #{order.Id} has been placed. Thank you.",
            sendAt: null,
            cancellationToken);

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken)
    {
        await TrySendAsync(
            order,
            OrderNotificationKind.OrderDispatched,
            $"Your eShop order #{order.Id} is on its way.",
            sendAt: null,
            cancellationToken);

        var sendAt = DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay);
        await TrySendAsync(
            order,
            OrderNotificationKind.DeliveryFollowUp,
            $"How did the delivery of eShop order #{order.Id} go?",
            sendAt,
            cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken)
    {
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        await TrySendAsync(
            order,
            OrderNotificationKind.OrderCancelled,
            $"Your eShop order #{order.Id} has been cancelled.",
            sendAt: null,
            cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>?> ListForOrderAsync(
        int orderId,
        string buyerId,
        bool isAdministrator,
        CancellationToken cancellationToken)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        if (!isAdministrator && !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            return null;
        }

        var notifications = await _notifications.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshStatusesAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<OrderNotification>>> ListForOrdersAsync(
        IReadOnlyCollection<int> orderIds,
        CancellationToken cancellationToken)
    {
        if (orderIds.Count == 0)
        {
            return new Dictionary<int, IReadOnlyList<OrderNotification>>();
        }

        var notifications = await _notifications.ListAsync(new NotificationsByOrderIdsSpecification(orderIds), cancellationToken);
        await RefreshStatusesAsync(notifications, cancellationToken);
        return notifications
            .GroupBy(n => n.OrderId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<OrderNotification>)g.ToList());
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        var existingAttempt = await _resendAttempts.FirstOrDefaultAsync(
            new NotificationResendAttemptSpecification(notificationId, idempotencyKey.Trim()),
            cancellationToken);
        if (existingAttempt is not null)
        {
            var previous = await _notifications.GetByIdAsync(existingAttempt.ResultNotificationId, cancellationToken);
            if (previous is not null)
            {
                await RefreshStatusesAsync(new[] { previous }, cancellationToken);
                return previous;
            }
        }

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new InvalidOperationException("Notification was not found.");

        if (source.ContentRedacted || string.IsNullOrWhiteSpace(source.Body))
        {
            throw new InvalidOperationException("The message content is no longer available to resend.");
        }

        await RefreshStatusesAsync(new[] { source }, cancellationToken);
        if (!string.IsNullOrEmpty(source.ProviderStatus) && ReachedStatuses.Contains(source.ProviderStatus))
        {
            throw new InvalidOperationException("The original message already reached the shopper.");
        }

        var destination = await ResolveActiveDestinationAsync(source.BuyerId, source.DestinationNumber, cancellationToken);
        if (destination is null)
        {
            throw new InvalidOperationException("The destination number is no longer on file for this shopper.");
        }

        var result = await SafeDispatchAsync(destination, source.Body, sendAt: null, cancellationToken);
        var resent = new OrderNotification(
            source.OrderId,
            source.BuyerId,
            OrderNotificationKind.Resend,
            destination,
            source.Body,
            result.ProviderSid,
            result.Status,
            sourceNotificationId: source.Id,
            errorCode: result.ErrorCode,
            errorMessage: result.ErrorMessage);

        await _notifications.AddAsync(resent, cancellationToken);
        await _resendAttempts.AddAsync(
            new NotificationResendAttempt(source.Id, idempotencyKey.Trim(), resent.Id),
            cancellationToken);

        _logger.LogInformation(
            "Resent notification {SourceNotificationId} as {NotificationId} for order {OrderId}",
            source.Id,
            resent.Id,
            source.OrderId);

        return resent;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new InvalidOperationException("Notification was not found.");

        if (!string.IsNullOrEmpty(notification.ProviderSid))
        {
            var snapshot = await _gateway.RedactBodyAsync(notification.ProviderSid, cancellationToken);
            if (snapshot is not null)
            {
                notification.ApplyProviderState(snapshot.Sid, snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage);
                if (!IsBodyDisposed(snapshot.Body))
                {
                    throw new MessagingProviderException(
                        "The provider still returns the message text after the disposal request.");
                }
            }
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Disposed message content for notification {NotificationId}", notificationId);
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var providerPage = await _gateway.ListSentFromConfiguredNumberAsync(from, to, cancellationToken);
        var providerMessages = providerPage.Messages;
        var local = await _notifications.ListAsync(new NotificationsCreatedInRangeSpecification(from, to), cancellationToken);

        var localBySid = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderSid))
            .GroupBy(n => n.ProviderSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matched = new List<ReconciledMessage>();
        var providerOnly = new List<ReconciledMessage>();
        var seenSids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var message in providerMessages)
        {
            if (string.IsNullOrEmpty(message.Sid))
            {
                continue;
            }

            seenSids.Add(message.Sid);
            if (localBySid.TryGetValue(message.Sid, out var localNote))
            {
                matched.Add(new ReconciledMessage(message.Sid, localNote.Id, message.Status, localNote.ProviderStatus, message.DateSent));
            }
            else
            {
                providerOnly.Add(new ReconciledMessage(message.Sid, null, message.Status, null, message.DateSent));
            }
        }

        var applicationOnly = local
            .Where(n => string.IsNullOrEmpty(n.ProviderSid) || !seenSids.Contains(n.ProviderSid!))
            .Select(n => new ReconciledMessage(n.ProviderSid, n.Id, null, n.ProviderStatus, null))
            .ToList();

        return new NotificationReconciliationReport(
            from,
            to,
            _gateway.ConfiguredFromNumber,
            providerPage.Truncated,
            matched,
            providerOnly,
            applicationOnly);
    }

    private async Task TrySendAsync(
        Order order,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        var destination = await GetActiveDestinationAsync(order.BuyerId, cancellationToken);
        if (destination is null)
        {
            _logger.LogInformation("Skipping {Kind} SMS for order {OrderId}; no contact number on file.", kind, order.Id);
            return;
        }

        var result = await SafeDispatchAsync(destination, body, sendAt, cancellationToken);
        var notification = new OrderNotification(
            order.Id,
            order.BuyerId,
            kind,
            destination,
            body,
            result.ProviderSid,
            result.Status,
            sendAt,
            errorCode: result.ErrorCode,
            errorMessage: result.ErrorMessage);

        await _notifications.AddAsync(notification, cancellationToken);
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notifications.ListAsync(new ScheduledFollowUpByOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            if (string.IsNullOrEmpty(followUp.ProviderSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _gateway.CancelScheduledAsync(followUp.ProviderSid, cancellationToken);
                if (snapshot is not null)
                {
                    followUp.ApplyProviderState(snapshot.Sid, snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage);
                    await _notifications.UpdateAsync(followUp, cancellationToken);
                }
            }
            catch (Exception ex) when (ex is MessagingProviderException or InvalidOperationException)
            {
                _logger.LogWarning(
                    "Failed to cancel scheduled follow-up {NotificationId} for order {OrderId}: {Message}",
                    followUp.Id,
                    orderId,
                    ex.Message);
            }
        }
    }

    private async Task<SmsDispatchResult> SafeDispatchAsync(
        string destination,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            return sendAt is null
                ? await _gateway.SendImmediateAsync(destination, body, cancellationToken)
                : await _gateway.ScheduleAsync(destination, body, sendAt.Value, cancellationToken);
        }
        catch (MessagingProviderException ex)
        {
            _logger.LogWarning("SMS dispatch failed for a shopper notification: {Message}", ex.Message);
            return new SmsDispatchResult(false, null, "failed", null, "dispatch_failed");
        }
    }

    private async Task RefreshStatusesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderSid))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(notification.ProviderStatus) && TerminalStatuses.Contains(notification.ProviderStatus))
            {
                continue;
            }

            try
            {
                var snapshot = await _gateway.FetchAsync(notification.ProviderSid, cancellationToken);
                if (snapshot is null)
                {
                    continue;
                }

                notification.ApplyProviderState(snapshot.Sid, snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage);
                if (notification.ContentRedacted)
                {
                    notification.MarkContentRedacted();
                }

                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (MessagingProviderException ex)
            {
                _logger.LogWarning(
                    "Failed to refresh provider status for notification {NotificationId}: {Message}",
                    notification.Id,
                    ex.Message);
            }
        }
    }

    private async Task<string?> GetActiveDestinationAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers.FirstOrDefault()?.CanonicalNumber;
    }

    private async Task<string?> ResolveActiveDestinationAsync(string buyerId, string previousDestination, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        if (numbers.Count == 0)
        {
            return null;
        }

        return numbers.FirstOrDefault(n => n.CanonicalNumber == previousDestination)?.CanonicalNumber
               ?? numbers[0].CanonicalNumber;
    }

    private static bool IsBodyDisposed(string? body)
        => string.IsNullOrEmpty(body) || string.IsNullOrWhiteSpace(body);
}
