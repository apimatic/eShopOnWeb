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

/// <summary>
/// Orchestrates the SMS notifications that go out as an order moves. All sending is
/// best-effort: a message that cannot be submitted is recorded as such and never propagated
/// as a failure of the underlying order operation, and a shopper with no number on file is
/// simply not messaged. Destination numbers and message bodies are never logged.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    // "A few days later" — comfortably inside the provider's 15-minute-to-35-day window.
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    // Statuses that mean a notification never actually went out to the carrier, so it is not
    // something eShop "believes it sent" for reconciliation purposes.
    private static readonly HashSet<string> NotActuallySentStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "scheduled", "canceled", OrderNotification.SubmissionFailedStatus };

    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        ISmsGateway smsGateway,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        var body = $"eShop: thanks! Your order #{orderId} has been placed.";
        foreach (var number in await GetNumbersAsync(buyerId, cancellationToken))
        {
            await SendImmediateAndRecordAsync(orderId, buyerId, number, NotificationKind.OrderPlaced, body, cancellationToken);
        }
    }

    public async Task NotifyOrderDispatchedAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        var dispatchedBody = $"eShop: good news - your order #{orderId} is on its way!";
        var followUpBody = $"eShop: how did the delivery of your order #{orderId} go? We'd love your feedback.";
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);

        foreach (var number in await GetNumbersAsync(buyerId, cancellationToken))
        {
            await SendImmediateAndRecordAsync(orderId, buyerId, number, NotificationKind.OrderDispatched, dispatchedBody, cancellationToken);
            await ScheduleFollowUpAndRecordAsync(orderId, buyerId, number, followUpBody, sendAt, cancellationToken);
        }
    }

    public async Task NotifyOrderCancelledAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        var body = $"eShop: your order #{orderId} has been cancelled. You have not been charged.";
        foreach (var number in await GetNumbersAsync(buyerId, cancellationToken))
        {
            await SendImmediateAndRecordAsync(orderId, buyerId, number, NotificationKind.OrderCancelled, body, cancellationToken);
        }

        // Call off any delivery follow-up queued at dispatch that has not yet gone out — asking
        // a customer how their delivery went for a cancelled order is exactly the incident this
        // prevents. This is driven off our own notification records, so it happens even if the
        // shopper has since removed the number.
        await CancelPendingFollowUpsAsync(orderId, cancellationToken);
    }

    public async Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // Under a key already used by a resend, hand back what that first attempt produced —
        // without sending a second message.
        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("Resend request for notification {NotificationId} under an existing idempotency key returned prior notification {ResendNotificationId}; no message sent.",
                notificationId, existing.Id);
            return existing;
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            return null;
        }

        if (original.ContentDisposed || original.Body is null)
        {
            throw new NotificationContentDisposedException(original.Id);
        }

        // Persist the resend record with its idempotency key *before* attempting to send, so a
        // repeat under the same key is recognised even if this send fails.
        var resend = original.CreateResend(idempotencyKey);
        resend = await _notificationRepository.AddAsync(resend, cancellationToken);

        await SubmitImmediateAsync(resend, cancellationToken);
        await _notificationRepository.UpdateAsync(resend, cancellationToken);
        return resend;
    }

    public async Task<OrderNotification?> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return null;
        }

        if (notification.ContentDisposed)
        {
            return notification;
        }

        // Redact at the provider first: only once the text is gone there do we clear it here.
        // If the provider call fails we let it surface, leaving the message intact so disposal
        // can be retried rather than silently reporting a success that did not happen.
        if (notification.ProviderMessageSid is not null)
        {
            await _smsGateway.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
        }

        notification.MarkContentDisposed();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Disposed of content for notification {NotificationId}.", notification.Id);
        return notification;
    }

    public async Task<IReadOnlyList<OrderNotification>> GetNotificationsForOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.ListAsync(
            new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshDeliveryStateAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task RefreshDeliveryStateAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken = default)
    {
        var changed = false;
        foreach (var notification in notifications)
        {
            if (notification.ProviderMessageSid is null)
            {
                continue;
            }

            try
            {
                var message = await _smsGateway.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                notification.UpdateDeliveryState(message.Status, message.ErrorCode);
                changed = true;
            }
            catch (Exception ex)
            {
                // A read failure must not stop us reporting the last-known outcome of the rest.
                _logger.LogWarning("Could not refresh delivery state for notification {NotificationId}: {Error}",
                    notification.Id, ex.Message);
            }
        }

        if (changed)
        {
            await PersistDeliveryStateAsync(notifications, cancellationToken);
        }
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _smsGateway.ListSentFromConfiguredNumberAsync(from, to, cancellationToken);

        // "What eShop believes it sent" is a notification that carries a provider id and was
        // actually pushed out — not one still scheduled or already called off before sending.
        var eShopNotifications = (await _notificationRepository.ListAsync(
                new OrderNotificationsSubmittedBetweenSpecification(from, to), cancellationToken))
            .Where(n => n.ProviderMessageSid is not null && !NotActuallySentStatuses.Contains(n.ProviderStatus))
            .ToList();

        var eShopBySid = eShopNotifications
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var report = new ReconciliationReport { From = from, To = to };
        var providerSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var message in providerMessages)
        {
            providerSids.Add(message.Sid);
            if (eShopBySid.TryGetValue(message.Sid, out var notification))
            {
                report.Matched.Add(new ReconciliationMatch(
                    message.Sid, notification.Id, notification.OrderId, message.Status, message.ErrorCode, message.DateSent));
            }
            else
            {
                report.ProviderOnly.Add(new ReconciliationProviderRecord(
                    message.Sid, message.Status, message.ErrorCode, message.DateSent));
            }
        }

        foreach (var notification in eShopNotifications)
        {
            if (!providerSids.Contains(notification.ProviderMessageSid!))
            {
                report.EShopOnly.Add(new ReconciliationEShopRecord(
                    notification.Id, notification.OrderId, notification.ProviderMessageSid, notification.ProviderStatus));
            }
        }

        return report;
    }

    private async Task<IReadOnlyList<string>> GetNumbersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumberRepository.ListAsync(
            new ContactNumbersByOwnerSpecification(buyerId), cancellationToken);
        return numbers.Select(n => n.PhoneNumber).ToList();
    }

    private async Task<OrderNotification> SendImmediateAndRecordAsync(
        int orderId, string buyerId, string toNumber, NotificationKind kind, string body, CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(orderId, buyerId, toNumber, kind, body);
        notification = await _notificationRepository.AddAsync(notification, cancellationToken);
        await SubmitImmediateAsync(notification, cancellationToken);
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        return notification;
    }

    private async Task SubmitImmediateAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var message = await _smsGateway.SendAsync(notification.ToPhoneNumber, notification.Body!, cancellationToken);
            notification.RecordSubmitted(message.Sid, message.Status);
            _logger.LogInformation("Submitted {Kind} notification {NotificationId} for order {OrderId}; provider status {Status}.",
                notification.Kind, notification.Id, notification.OrderId, message.Status);
        }
        catch (Exception ex)
        {
            notification.RecordSubmissionFailed();
            _logger.LogWarning("Could not submit {Kind} notification {NotificationId} for order {OrderId}: {Error}",
                notification.Kind, notification.Id, notification.OrderId, ex.Message);
        }
    }

    private async Task ScheduleFollowUpAndRecordAsync(
        int orderId, string buyerId, string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(orderId, buyerId, toNumber, NotificationKind.DeliveryFollowUp, body);
        notification = await _notificationRepository.AddAsync(notification, cancellationToken);
        try
        {
            var message = await _smsGateway.ScheduleAsync(toNumber, body, sendAt, cancellationToken);
            notification.RecordSubmitted(message.Sid, message.Status, sendAt);
            _logger.LogInformation("Scheduled delivery follow-up {NotificationId} for order {OrderId}; provider status {Status}.",
                notification.Id, orderId, message.Status);
        }
        catch (Exception ex)
        {
            notification.RecordSubmissionFailed();
            _logger.LogWarning("Could not schedule delivery follow-up {NotificationId} for order {OrderId}: {Error}",
                notification.Id, orderId, ex.Message);
        }

        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _notificationRepository.ListAsync(
            new OrderNotificationsByOrderSpecification(orderId), cancellationToken);

        foreach (var followUp in notifications.Where(n => n.IsPendingScheduledFollowUp))
        {
            try
            {
                await _smsGateway.CancelScheduledAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.MarkScheduleCanceled();
                await _notificationRepository.UpdateAsync(followUp, cancellationToken);
                _logger.LogInformation("Called off scheduled delivery follow-up {NotificationId} for cancelled order {OrderId}.",
                    followUp.Id, orderId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not call off scheduled delivery follow-up {NotificationId} for order {OrderId}: {Error}",
                    followUp.Id, orderId, ex.Message);
            }
        }
    }

    private async Task PersistDeliveryStateAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
    }
}
