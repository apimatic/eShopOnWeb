using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.ApplicationCore.Twilio;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    // The delivery follow-up is queued with the provider this long after dispatch.
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    // Provider outcomes that will not change again; no need to re-fetch them.
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered", "undelivered", "failed", "canceled"
    };

    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly ITwilioMessagingClient _messagingClient;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notificationRepository,
        IRepository<ContactNumber> contactNumberRepository,
        ITwilioMessagingClient messagingClient,
        IAppLogger<OrderNotificationService> logger)
    {
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _messagingClient = messagingClient;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: your order #{order.Id} has been placed. Total: {order.Total():C}. Thank you for shopping with us!";
        await SendToShopperAsync(order, NotificationKind.OrderPlaced, body, null, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: good news — your order #{order.Id} has been dispatched and is on its way!";
        await SendToShopperAsync(order, NotificationKind.OrderDispatched, body, null, cancellationToken);

        // Queue the delivery follow-up with the provider itself (ScheduleType=fixed);
        // nothing is held in this application to be sent later by a timer of its own.
        var followUpBody = $"eShopOnWeb: your order #{order.Id} should have arrived by now. How did the delivery go?";
        await SendToShopperAsync(order, NotificationKind.DeliveryFollowUp, followUpBody, DateTimeOffset.UtcNow.Add(FollowUpDelay), cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: your order #{order.Id} has been cancelled. If you did not request this, please contact support.";
        await SendToShopperAsync(order, NotificationKind.OrderCancelled, body, null, cancellationToken);

        // A follow-up that has not yet gone out must never reach the shopper.
        var scheduled = await _notificationRepository.ListAsync(new ScheduledNotificationsForOrderSpecification(order.Id), cancellationToken);
        foreach (var notification in scheduled)
        {
            await CancelScheduledAsync(notification, cancellationToken);
        }
    }

    public async Task CancelScheduledMessagesForContactNumberAsync(int contactNumberId, CancellationToken cancellationToken = default)
    {
        var scheduled = await _notificationRepository.ListAsync(new ScheduledNotificationsForContactNumberSpecification(contactNumberId), cancellationToken);
        foreach (var notification in scheduled)
        {
            await CancelScheduledAsync(notification, cancellationToken);
        }
    }

    public async Task<ResendNotificationResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var existing = await _notificationRepository.FirstOrDefaultAsync(new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (existing != null)
        {
            return new ResendNotificationResult(ResendOutcome.Duplicate, existing);
        }

        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification == null)
        {
            return new ResendNotificationResult(ResendOutcome.NotFound, null);
        }
        if (notification.ContentDisposed || notification.Body == null)
        {
            return new ResendNotificationResult(ResendOutcome.ContentDisposed, notification);
        }

        var contactNumber = await _contactNumberRepository.GetByIdAsync(notification.ContactNumberId, cancellationToken);
        if (contactNumber == null)
        {
            // The number was removed; nothing may be sent to it again.
            return new ResendNotificationResult(ResendOutcome.DestinationRemoved, notification);
        }

        var resend = new OrderNotification(notification.OrderId, notification.BuyerId, notification.ContactNumberId,
            notification.ToNumber, notification.Kind, notification.Body,
            resendOfNotificationId: notification.Id, idempotencyKey: idempotencyKey);

        await SendAndRecordAsync(resend, null, cancellationToken);
        return new ResendNotificationResult(ResendOutcome.Sent, resend);
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification == null)
        {
            return false;
        }

        if (!notification.ContentDisposed && notification.MessageSid != null)
        {
            // Redact at the provider so the text is no longer retrievable there either.
            // A just-created message can briefly 404 while it propagates, so retry;
            // a persistent 404 means the provider no longer holds the message at all.
            const int maxAttempts = 4;
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await _messagingClient.RedactMessageBodyAsync(notification.MessageSid, cancellationToken);
                    break;
                }
                catch (TwilioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound && attempt < maxAttempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch (TwilioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    // The provider no longer holds the message; the disposal goal is met.
                    break;
                }
                catch (Exception ex) when ((ex is TwilioApiException or HttpRequestException) && attempt < maxAttempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
            }
        }

        notification.MarkContentDisposed();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        foreach (var notification in notifications)
        {
            await RefreshOutcomeAsync(notification, cancellationToken);
        }
        return notifications;
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _messagingClient.ListMessagesAsync(from, to, cancellationToken);
        var localNotifications = await _notificationRepository.ListAsync(new NotificationsInRangeSpecification(from, to), cancellationToken);

        var localBySid = localNotifications
            .Where(n => n.MessageSid != null)
            .GroupBy(n => n.MessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var report = new NotificationReconciliationReport { From = from, To = to };
        var matchedSids = new HashSet<string>();

        foreach (var message in providerMessages)
        {
            if (message.Sid != null && localBySid.TryGetValue(message.Sid, out var local))
            {
                matchedSids.Add(message.Sid);
                report.Entries.Add(new ReconciliationEntry
                {
                    MessageSid = message.Sid,
                    NotificationId = local.Id,
                    OrderId = local.OrderId,
                    To = message.To,
                    ProviderStatus = message.Status,
                    LocalStatus = local.Status,
                    DateSent = message.DateSent,
                    Match = ReconciliationMatch.Matched
                });
            }
            else
            {
                report.Entries.Add(new ReconciliationEntry
                {
                    MessageSid = message.Sid,
                    To = message.To,
                    ProviderStatus = message.Status,
                    DateSent = message.DateSent,
                    Match = ReconciliationMatch.MissingLocally
                });
            }
        }

        foreach (var local in localNotifications)
        {
            if (local.MessageSid == null || !matchedSids.Contains(local.MessageSid))
            {
                report.Entries.Add(new ReconciliationEntry
                {
                    MessageSid = local.MessageSid,
                    NotificationId = local.Id,
                    OrderId = local.OrderId,
                    To = local.ToNumber,
                    LocalStatus = local.Status,
                    DateSent = null,
                    Match = ReconciliationMatch.MissingAtProvider
                });
            }
        }

        report.MatchedCount = report.Entries.Count(e => e.Match == ReconciliationMatch.Matched);
        report.MissingLocallyCount = report.Entries.Count(e => e.Match == ReconciliationMatch.MissingLocally);
        report.MissingAtProviderCount = report.Entries.Count(e => e.Match == ReconciliationMatch.MissingAtProvider);
        report.Entries = report.Entries.OrderBy(e => e.DateSent ?? DateTimeOffset.MinValue).ToList();
        return report;
    }

    private async Task SendToShopperAsync(Order order, NotificationKind kind, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        var contactNumbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
        foreach (var contactNumber in contactNumbers)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, contactNumber.Id, contactNumber.PhoneNumber, kind, body, scheduledFor: sendAt);
            await SendAndRecordAsync(notification, sendAt, cancellationToken);
        }
    }

    private async Task SendAndRecordAsync(OrderNotification notification, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        try
        {
            var message = await _messagingClient.CreateMessageAsync(notification.ToNumber, notification.Body!, sendAt, cancellationToken);
            notification.MarkSent(message.Sid!, message.Status ?? "queued");
        }
        catch (Exception ex) when (ex is TwilioApiException or HttpRequestException or TaskCanceledException)
        {
            // A message that cannot be sent must never fail the underlying operation.
            // Provider error text can embed the destination number, so log only the error code.
            _logger.LogWarning("SMS notification {0} for order {1} could not be submitted to the provider (error code {2}).",
                notification.Kind, notification.OrderId, (ex as TwilioApiException)?.TwilioErrorCode?.ToString() ?? "n/a");
            notification.MarkSendFailed((ex as TwilioApiException)?.TwilioErrorMessage ?? "Provider request failed.");
        }

        await _notificationRepository.AddAsync(notification, cancellationToken);
    }

    private async Task CancelScheduledAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        // A just-created scheduled message can briefly 404 at the provider while it
        // propagates; retry so a follow-up that must not go out is reliably called off.
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var message = await _messagingClient.CancelScheduledMessageAsync(notification.MessageSid!, cancellationToken);
                notification.UpdateDeliveryOutcome(message.Status ?? "canceled", message.ErrorCode, message.ErrorMessage);
                break;
            }
            catch (Exception ex) when (ex is TwilioApiException or HttpRequestException or TaskCanceledException)
            {
                if (attempt >= maxAttempts)
                {
                    _logger.LogWarning("Scheduled provider message for notification {0} could not be cancelled after {1} attempts (error code {2}).",
                        notification.Id, attempt, (ex as TwilioApiException)?.TwilioErrorCode?.ToString() ?? "n/a");
                    break;
                }
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    private async Task RefreshOutcomeAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (notification.MessageSid == null || TerminalStatuses.Contains(notification.Status))
        {
            return;
        }

        try
        {
            var message = await _messagingClient.FetchMessageAsync(notification.MessageSid, cancellationToken);
            notification.UpdateDeliveryOutcome(message.Status ?? notification.Status, message.ErrorCode, message.ErrorMessage);
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex) when (ex is TwilioApiException or HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("Delivery outcome for notification {0} could not be refreshed from the provider (error code {1}).",
                notification.Id, (ex as TwilioApiException)?.TwilioErrorCode?.ToString() ?? "n/a");
        }
    }
}
