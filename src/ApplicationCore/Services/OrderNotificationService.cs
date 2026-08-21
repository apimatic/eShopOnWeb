using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(4);

    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<ResendIdempotencyRecord> _idempotency;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notifications,
        IRepository<ContactNumber> contactNumbers,
        IRepository<ResendIdempotencyRecord> idempotency,
        ISmsGateway smsGateway,
        IAppLogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _idempotency = idempotency;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(int orderId, string buyerId, CancellationToken cancellationToken)
        => TrySendAsync(orderId, buyerId, NotificationKind.OrderPlaced,
            $"Your eShop order #{orderId} has been placed.", cancellationToken);

    public async Task NotifyOrderDispatchedAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        await TrySendAsync(orderId, buyerId, NotificationKind.OrderDispatched,
            $"Your eShop order #{orderId} is on its way.", cancellationToken);

        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        await TrySendAsync(orderId, buyerId, NotificationKind.DeliveryFollowUp,
            $"How did the delivery of eShop order #{orderId} go?", cancellationToken, sendAt);
    }

    public async Task NotifyOrderCancelledAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        await CancelPendingFollowUpsAsync(orderId, cancellationToken);
        await TrySendAsync(orderId, buyerId, NotificationKind.OrderCancelled,
            $"Your eShop order #{orderId} has been cancelled.", cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(
        int orderId,
        string? buyerId,
        bool allowAnyBuyer,
        CancellationToken cancellationToken)
    {
        var notifications = await _notifications.ListAsync(
            new OrderNotificationsByOrderSpecification(orderId), cancellationToken);

        if (!allowAnyBuyer)
        {
            if (string.IsNullOrEmpty(buyerId) || notifications.Any(n => n.BuyerId != buyerId) || notifications.Count == 0)
            {
                // Still allow an empty list for an order the caller owns; ownership is checked by the caller via the order.
                notifications = notifications.Where(n => n.BuyerId == buyerId).ToList();
            }
        }

        await RefreshStatusesAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new NotificationOperationException("An idempotency key is required.");
        }

        var existingKey = await _idempotency.FirstOrDefaultAsync(
            new ResendIdempotencySpecification(notificationId, idempotencyKey.Trim()), cancellationToken);
        if (existingKey is not null)
        {
            var previous = await _notifications.GetByIdAsync(existingKey.ResultNotificationId, cancellationToken);
            if (previous is not null)
            {
                return previous;
            }
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(OrderNotification), notificationId);

        await RefreshStatusesAsync(new[] { original }, cancellationToken);

        if (!original.DidNotReachShopper())
        {
            throw new NotificationOperationException("Only messages that did not reach the shopper can be re-sent.");
        }

        if (original.ContentRedacted || string.IsNullOrWhiteSpace(original.Body))
        {
            throw new NotificationOperationException("The original message content is no longer available to re-send.");
        }

        var destination = await ResolveActiveDestinationAsync(original.BuyerId, original.DestinationNumber, cancellationToken);
        var resend = new OrderNotification(original.OrderId, original.BuyerId, NotificationKind.Resend, destination, original.Body);
        resend.MarkResendOf(original.Id);
        resend = await _notifications.AddAsync(resend, cancellationToken);

        if (destination is null)
        {
            resend.ApplyProviderResult(null, null, null, null, null, "No registered destination is available.");
            await _notifications.UpdateAsync(resend, cancellationToken);
        }
        else
        {
            await DeliverAsync(resend, destination, original.Body, scheduledSendAt: null, cancellationToken);
        }

        var record = new ResendIdempotencyRecord(original.Id, idempotencyKey.Trim(), resend.Id);
        await _idempotency.AddAsync(record, cancellationToken);
        return resend;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(OrderNotification), notificationId);

        if (!string.IsNullOrEmpty(notification.ProviderSid))
        {
            try
            {
                var redacted = await _smsGateway.RedactBodyAsync(notification.ProviderSid, cancellationToken);
                notification.ApplyProviderResult(
                    redacted.Sid ?? notification.ProviderSid,
                    redacted.Status ?? notification.ProviderStatus,
                    redacted.ErrorCode,
                    redacted.ErrorMessage,
                    redacted.DateSent,
                    redacted.FailureReason);
            }
            catch (SmsProviderException)
            {
                _logger.LogWarning("Provider content disposal failed for notification {NotificationId}.", notification.Id);
                throw;
            }
        }

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (to <= from)
        {
            throw new NotificationOperationException("The reconciliation range 'to' must be after 'from'.");
        }

        var providerMessages = await _smsGateway.ListSentFromAsync(from, to, cancellationToken);
        var providerSids = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .Select(m => m.Sid!)
            .ToList();

        var localInRange = await _notifications.ListAsync(
            new OrderNotificationsInDateRangeSpecification(from, to), cancellationToken);
        var localBySid = providerSids.Count == 0
            ? new List<OrderNotification>()
            : await _notifications.ListAsync(
                new OrderNotificationsByProviderSidsSpecification(providerSids), cancellationToken);

        var localBySidLookup = localBySid
            .Concat(localInRange)
            .Where(n => !string.IsNullOrEmpty(n.ProviderSid))
            .GroupBy(n => n.ProviderSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciledMessage>();
        var providerOnly = new List<ReconciledMessage>();

        foreach (var provider in providerMessages)
        {
            if (string.IsNullOrEmpty(provider.Sid))
            {
                continue;
            }

            if (localBySidLookup.TryGetValue(provider.Sid, out var local))
            {
                matched.Add(ToReconciled(local, provider, "matched"));
            }
            else
            {
                providerOnly.Add(new ReconciledMessage(
                    null,
                    provider.Sid,
                    provider.Status,
                    provider.Direction,
                    provider.DateSent,
                    provider.ErrorCode,
                    provider.ErrorMessage,
                    "provider"));
            }
        }

        var providerSidSet = providerSids.ToHashSet();
        var eshopOnly = localInRange
            .Where(n => string.IsNullOrEmpty(n.ProviderSid) || !providerSidSet.Contains(n.ProviderSid))
            .Select(n => ToReconciled(n, null, "eshop"))
            .ToList();

        return new NotificationReconciliationReport(from, to, _smsGateway.FromNumber, matched, providerOnly, eshopOnly);
    }

    public async Task RefreshStatusesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderSid) || notification.IsTerminalStatus())
            {
                continue;
            }

            try
            {
                var latest = await _smsGateway.FetchAsync(notification.ProviderSid, cancellationToken);
                notification.ApplyProviderResult(
                    latest.Sid ?? notification.ProviderSid,
                    latest.Status,
                    latest.ErrorCode,
                    latest.ErrorMessage,
                    latest.DateSent,
                    latest.FailureReason);
                if (latest.Body == string.Empty || latest.Body is null && notification.ContentRedacted)
                {
                    notification.RedactContent();
                }
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (SmsProviderException)
            {
                _logger.LogWarning("Could not refresh provider status for notification {NotificationId}.", notification.Id);
            }
        }
    }

    private async Task TrySendAsync(
        int orderId,
        string buyerId,
        NotificationKind kind,
        string body,
        CancellationToken cancellationToken,
        DateTimeOffset? scheduledSendAt = null)
    {
        var destination = await ResolveActiveDestinationAsync(buyerId, preferred: null, cancellationToken);
        var notification = new OrderNotification(orderId, buyerId, kind, destination, body);
        if (scheduledSendAt.HasValue)
        {
            notification.MarkScheduled(scheduledSendAt.Value);
        }

        notification = await _notifications.AddAsync(notification, cancellationToken);

        if (destination is null)
        {
            notification.ApplyProviderResult(null, null, null, null, null, "No registered destination is available.");
            await _notifications.UpdateAsync(notification, cancellationToken);
            return;
        }

        await DeliverAsync(notification, destination, body, scheduledSendAt, cancellationToken);
    }

    private async Task DeliverAsync(
        OrderNotification notification,
        string destination,
        string body,
        DateTimeOffset? scheduledSendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = scheduledSendAt.HasValue
                ? await _smsGateway.ScheduleAsync(destination, body, scheduledSendAt.Value, cancellationToken)
                : await _smsGateway.SendAsync(destination, body, cancellationToken);

            notification.ApplyProviderResult(
                result.Sid,
                result.Status,
                result.ErrorCode,
                result.ErrorMessage,
                result.DateSent,
                result.FailureReason);
        }
        catch (SmsProviderException ex)
        {
            _logger.LogWarning("Order notification {Kind} for order {OrderId} could not be sent ({Status}).",
                notification.Kind, notification.OrderId, (int?)ex.StatusCode ?? 0);
            notification.ApplyProviderResult(null, null, (int?)ex.StatusCode, null, null, ex.Message);
        }

        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var pending = await _notifications.ListAsync(new ScheduledFollowUpsByOrderSpecification(orderId), cancellationToken);
        foreach (var notification in pending)
        {
            if (string.IsNullOrEmpty(notification.ProviderSid))
            {
                continue;
            }

            try
            {
                var cancelled = await _smsGateway.CancelScheduledAsync(notification.ProviderSid, cancellationToken);
                notification.ApplyProviderResult(
                    cancelled.Sid ?? notification.ProviderSid,
                    cancelled.Status,
                    cancelled.ErrorCode,
                    cancelled.ErrorMessage,
                    cancelled.DateSent,
                    cancelled.FailureReason);
            }
            catch (SmsProviderException ex)
            {
                _logger.LogWarning("Could not cancel scheduled follow-up {NotificationId} ({Status}).",
                    notification.Id, (int?)ex.StatusCode ?? 0);
                notification.ApplyProviderResult(
                    notification.ProviderSid,
                    notification.ProviderStatus,
                    (int?)ex.StatusCode,
                    null,
                    notification.DateSent,
                    ex.Message);
            }

            await _notifications.UpdateAsync(notification, cancellationToken);
        }
    }

    private async Task<string?> ResolveActiveDestinationAsync(
        string buyerId,
        string? preferred,
        CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        if (numbers.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(preferred))
        {
            var match = numbers.FirstOrDefault(n => n.CanonicalNumber == preferred);
            if (match is not null)
            {
                return match.CanonicalNumber;
            }
        }

        return numbers[0].CanonicalNumber;
    }

    private static ReconciledMessage ToReconciled(OrderNotification local, SmsMessageResult? provider, string source)
    {
        return new ReconciledMessage(
            local.Id,
            provider?.Sid ?? local.ProviderSid,
            provider?.Status ?? local.ProviderStatus,
            provider?.Direction,
            provider?.DateSent ?? local.DateSent,
            provider?.ErrorCode ?? local.ProviderErrorCode,
            provider?.ErrorMessage ?? local.ProviderErrorMessage,
            source);
    }
}
