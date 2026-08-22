using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private static readonly HashSet<string> TerminalFailureStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "failed",
        "undelivered",
        "canceled",
        "send_failed"
    };

    private readonly IRepository<OrderNotification> _notifications;
    private readonly IContactNumberService _contactNumbers;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notifications,
        IContactNumberService contactNumbers,
        ISmsGateway smsGateway,
        IAppLogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(int orderId, string buyerId, CancellationToken cancellationToken = default) =>
        TrySendAsync(orderId, buyerId, NotificationPurpose.OrderPlaced,
            $"eShopOnWeb: your order #{orderId} has been placed. Thank you for shopping with us.",
            scheduledFor: null, cancellationToken);

    public async Task NotifyOrderDispatchedAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        await TrySendAsync(orderId, buyerId, NotificationPurpose.OrderDispatched,
            $"eShopOnWeb: your order #{orderId} is on its way.",
            scheduledFor: null, cancellationToken);

        var followUpAt = DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay);
        await TrySendAsync(orderId, buyerId, NotificationPurpose.DeliveryFollowUp,
            $"eShopOnWeb: how did the delivery of order #{orderId} go?",
            followUpAt, cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        await CancelPendingFollowUpsAsync(orderId, cancellationToken);

        await TrySendAsync(orderId, buyerId, NotificationPurpose.OrderCancelled,
            $"eShopOnWeb: your order #{orderId} has been cancelled.",
            scheduledFor: null, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotificationView>> ListForOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notifications.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        await SyncFromProviderAsync(notifications, cancellationToken);
        return notifications.Select(ToView).ToList();
    }

    public async Task<IReadOnlyList<OrderNotificationView>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notifications.ListAsync(new NotificationsByBuyerSpecification(buyerId), cancellationToken);
        await SyncFromProviderAsync(notifications, cancellationToken);
        return notifications.Select(ToView).ToList();
    }

    public async Task<OrderNotificationView> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new InvalidOrderStateException("An idempotency key is required to resend a notification.");
        }

        var existingResend = await _notifications.FirstOrDefaultAsync(
            new ResendByIdempotencyKeySpecification(notificationId, idempotencyKey.Trim()), cancellationToken);
        if (existingResend is not null)
        {
            await SyncFromProviderAsync(new[] { existingResend }, cancellationToken);
            return ToView(existingResend);
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotificationNotFoundException(notificationId);

        await SyncFromProviderAsync(new[] { original }, cancellationToken);

        if (original.ContentRedacted || string.IsNullOrEmpty(original.Body))
        {
            throw new InvalidOrderStateException("The original message content has been disposed of and cannot be resent.");
        }

        if (!TerminalFailureStatuses.Contains(original.ProviderStatus))
        {
            throw new InvalidOrderStateException("Only notifications that did not reach the shopper can be resent.");
        }

        var stillRegistered = await _contactNumbers.IsDestinationStillRegisteredAsync(
            original.BuyerId, original.ContactNumberId, cancellationToken);
        if (!stillRegistered)
        {
            throw new InvalidOrderStateException("The destination number is no longer on file and cannot be messaged again.");
        }

        var resend = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            original.ContactNumberId,
            original.DestinationNumber,
            NotificationPurpose.Resend,
            original.Body);
        resend.RecordResend(original.Id, idempotencyKey.Trim());
        resend = await _notifications.AddAsync(resend, cancellationToken);

        await DispatchToProviderAsync(resend, scheduledFor: null, cancellationToken);
        return ToView(resend);
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotificationNotFoundException(notificationId);

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            try
            {
                var updated = await _smsGateway.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
                notification.ApplyProviderState(updated.Status, updated.ErrorCode, updated.ErrorMessage);
                if (string.IsNullOrEmpty(updated.Body))
                {
                    notification.RedactContent();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to redact provider content for notification {NotificationId}: {Error}", notification.Id, ex.GetType().Name);
                throw;
            }
        }

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new InvalidOrderStateException("'to' must be on or after 'from'.");
        }

        var providerMessages = await _smsGateway.ListSentFromConfiguredNumberAsync(from, to, cancellationToken);
        var providerSids = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .Select(m => m.Sid)
            .Distinct()
            .ToArray();

        var bySid = providerSids.Length == 0
            ? new List<OrderNotification>()
            : await _notifications.ListAsync(new NotificationsByProviderSidsSpecification(providerSids), cancellationToken);

        var inRange = await _notifications.ListAsync(new NotificationsInCreatedRangeSpecification(from, to), cancellationToken);

        var localBySid = bySid.Concat(inRange)
            .GroupBy(n => n.Id)
            .Select(g => g.First())
            .ToList();

        var matched = new List<NotificationReconciliationItem>();
        var providerOnly = new List<NotificationReconciliationItem>();
        var localOnly = new List<NotificationReconciliationItem>();

        foreach (var message in providerMessages)
        {
            var local = localBySid.FirstOrDefault(n =>
                string.Equals(n.ProviderMessageSid, message.Sid, StringComparison.Ordinal));
            if (local is null)
            {
                providerOnly.Add(new NotificationReconciliationItem(
                    message.Sid, null, "provider", message.Status, null, message.DateCreated, null));
            }
            else
            {
                matched.Add(new NotificationReconciliationItem(
                    message.Sid, local.Id, "matched", message.Status, local.ProviderStatus, message.DateCreated, local.CreatedAt));
            }
        }

        foreach (var local in localBySid)
        {
            if (string.IsNullOrEmpty(local.ProviderMessageSid) || !providerSids.Contains(local.ProviderMessageSid))
            {
                localOnly.Add(new NotificationReconciliationItem(
                    local.ProviderMessageSid, local.Id, "local", null, local.ProviderStatus, null, local.CreatedAt));
            }
        }

        return new NotificationReconciliationReport(from, to, matched, providerOnly, localOnly);
    }

    private async Task TrySendAsync(
        int orderId,
        string buyerId,
        NotificationPurpose purpose,
        string body,
        DateTimeOffset? scheduledFor,
        CancellationToken cancellationToken)
    {
        try
        {
            var destination = await _contactNumbers.GetPreferredForBuyerAsync(buyerId, cancellationToken);
            if (destination is null)
            {
                _logger.LogInformation("Skipping {Purpose} SMS for order {OrderId}; shopper has no number on file.", purpose, orderId);
                return;
            }

            var notification = new OrderNotification(
                orderId,
                buyerId,
                destination.Id,
                destination.CanonicalNumber,
                purpose,
                body);
            if (scheduledFor.HasValue)
            {
                notification.RecordScheduledFor(scheduledFor.Value);
            }

            notification = await _notifications.AddAsync(notification, cancellationToken);
            await DispatchToProviderAsync(notification, scheduledFor, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("SMS notification {Purpose} for order {OrderId} did not complete: {Error}", purpose, orderId, ex.GetType().Name);
        }
    }

    private async Task DispatchToProviderAsync(OrderNotification notification, DateTimeOffset? scheduledFor, CancellationToken cancellationToken)
    {
        try
        {
            SmsMessage result;
            if (scheduledFor.HasValue)
            {
                result = await _smsGateway.ScheduleAsync(notification.DestinationNumber, notification.Body!, scheduledFor.Value, cancellationToken);
            }
            else
            {
                result = await _smsGateway.SendAsync(notification.DestinationNumber, notification.Body!, cancellationToken);
            }

            notification.RecordProviderAccepted(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage);
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            notification.RecordSendFailure(ex.GetType().Name);
            await _notifications.UpdateAsync(notification, cancellationToken);
            _logger.LogWarning("Provider send failed for notification {NotificationId}: {Error}", notification.Id, ex.GetType().Name);
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        try
        {
            var followUps = await _notifications.ListAsync(new ScheduledFollowUpsForOrderSpecification(orderId), cancellationToken);
            foreach (var followUp in followUps)
            {
                if (string.IsNullOrEmpty(followUp.ProviderMessageSid))
                {
                    continue;
                }

                try
                {
                    var updated = await _smsGateway.CancelScheduledAsync(followUp.ProviderMessageSid, cancellationToken);
                    followUp.ApplyProviderState(updated.Status, updated.ErrorCode, updated.ErrorMessage);
                    await _notifications.UpdateAsync(followUp, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Could not cancel scheduled follow-up {NotificationId}: {Error}", followUp.Id, ex.GetType().Name);
                    try
                    {
                        var current = await _smsGateway.FetchAsync(followUp.ProviderMessageSid, cancellationToken);
                        followUp.ApplyProviderState(current.Status, current.ErrorCode, current.ErrorMessage);
                        await _notifications.UpdateAsync(followUp, cancellationToken);
                    }
                    catch (Exception fetchEx)
                    {
                        _logger.LogWarning("Could not refresh scheduled follow-up {NotificationId}: {Error}", followUp.Id, fetchEx.GetType().Name);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Cancel-follow-up pass failed for order {OrderId}: {Error}", orderId, ex.GetType().Name);
        }
    }

    private async Task SyncFromProviderAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var current = await _smsGateway.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                notification.ApplyProviderState(current.Status, current.ErrorCode, current.ErrorMessage);
                if (notification.ContentRedacted || string.IsNullOrEmpty(current.Body))
                {
                    notification.RedactContent();
                }
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not refresh provider status for notification {NotificationId}: {Error}", notification.Id, ex.GetType().Name);
            }
        }
    }

    private static OrderNotificationView ToView(OrderNotification notification) =>
        new(
            notification.Id,
            notification.OrderId,
            notification.Purpose,
            notification.ContentRedacted ? null : notification.Body,
            notification.ContentRedacted,
            notification.ProviderMessageSid,
            notification.ProviderStatus,
            notification.ProviderErrorCode,
            notification.ProviderErrorMessage,
            notification.ScheduledFor,
            notification.CreatedAt,
            notification.SendFailureReason);
}
