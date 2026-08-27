using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    // The follow-up is queued with the provider for this long after dispatch.
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ITwilioMessagingClient _messagingClient;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        ITwilioMessagingClient messagingClient,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _messagingClient = messagingClient;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShop: your order #{order.Id} has been placed. Total: {order.Total().ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}. Thank you for shopping with us!";
        await SendToShopperAsync(order, NotificationType.OrderPlaced, body, null, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShop: good news — your order #{order.Id} is on its way!";
        await SendToShopperAsync(order, NotificationType.OrderDispatched, body, null, cancellationToken);

        var followUpBody = $"eShop: how did the delivery of your order #{order.Id} go? We'd love to know.";
        await SendToShopperAsync(order, NotificationType.DeliveryFollowUp, followUpBody,
            DateTimeOffset.UtcNow.Add(FollowUpDelay), cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        // Call off any follow-up that has not yet gone out: a cancelled order must
        // never be followed by "how did the delivery went?".
        var scheduled = await _notificationRepository.ListAsync(
            new ScheduledFollowUpsByOrderSpecification(order.Id), cancellationToken);
        foreach (var followUp in scheduled)
        {
            try
            {
                var cancelled = await _messagingClient.CancelScheduledMessageAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.UpdateProviderState(cancelled.Status, cancelled.ErrorCode, cancelled.ErrorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not cancel scheduled follow-up notification {NotificationId} for order {OrderId} at the provider: {Error}",
                    followUp.Id, order.Id, ex.Message);
                followUp.UpdateProviderState("cancel-requested", followUp.ProviderErrorCode, followUp.ProviderErrorMessage);
            }
            await _notificationRepository.UpdateAsync(followUp, cancellationToken);
        }

        var body = $"eShop: your order #{order.Id} has been cancelled. Please contact support if this is unexpected.";
        await SendToShopperAsync(order, NotificationType.OrderCancelled, body, null, cancellationToken);
    }

    public async Task<ResendResult> ResendAsync(OrderNotification original, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (existing is not null)
        {
            return new ResendResult(existing, alreadyExisted: true);
        }

        if (original.ContentRedacted || original.Body is null)
        {
            throw new NotificationContentRedactedException(original.Id);
        }

        // A removed contact number must never be sent to again.
        var stillRegistered = await _contactNumberRepository.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndNumberSpecification(original.BuyerId, original.ToNumber), cancellationToken);
        if (stillRegistered is null)
        {
            throw new NotificationDestinationRemovedException(original.Id);
        }

        var resend = new OrderNotification(original.OrderId, original.BuyerId, original.ToNumber,
            original.Body, NotificationType.Resend, null, idempotencyKey);
        await SendAndRecordAsync(resend, null, cancellationToken);
        return new ResendResult(resend, alreadyExisted: false);
    }

    public async Task RedactContentAsync(OrderNotification notification, CancellationToken cancellationToken = default)
    {
        if (notification.ProviderMessageSid is not null)
        {
            await _messagingClient.RedactMessageBodyAsync(notification.ProviderMessageSid, cancellationToken);
        }
        notification.MarkContentRedacted();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    public async Task RefreshStatusAsync(OrderNotification notification, CancellationToken cancellationToken = default)
    {
        if (notification.ProviderMessageSid is null)
        {
            return;
        }

        try
        {
            var message = await _messagingClient.FetchMessageAsync(notification.ProviderMessageSid, cancellationToken);
            notification.UpdateProviderState(message.Status, message.ErrorCode, message.ErrorMessage);
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not refresh status of notification {NotificationId} from the provider: {Error}",
                notification.Id, ex.Message);
        }
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Only this application's own sending number: the account carries other traffic.
        var providerMessages = await _messagingClient.ListMessagesAsync(
            _messagingClient.FromNumber, from, to, cancellationToken);

        var localNotifications = await _notificationRepository.ListAsync(
            new OrderNotificationsInRangeSpecification(from, to), cancellationToken);

        var localBySid = localNotifications
            .Where(n => n.ProviderMessageSid is not null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var report = new ReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = _messagingClient.FromNumber,
            ProviderMessageCount = providerMessages.Count,
            LocalNotificationCount = localNotifications.Count
        };

        var matchedSids = new HashSet<string>();
        foreach (var message in providerMessages)
        {
            if (localBySid.TryGetValue(message.Sid, out var local))
            {
                matchedSids.Add(message.Sid);
                local.UpdateProviderState(message.Status, message.ErrorCode, message.ErrorMessage);
                await _notificationRepository.UpdateAsync(local, cancellationToken);
                report.Entries.Add(new ReconciliationEntry
                {
                    ProviderMessageSid = message.Sid,
                    NotificationId = local.Id,
                    Disposition = ReconciliationDisposition.Matched,
                    ProviderStatus = message.Status,
                    ProviderErrorCode = message.ErrorCode,
                    DateSent = message.DateSent,
                    RecordedAt = local.CreatedAt
                });
            }
            else
            {
                report.Entries.Add(new ReconciliationEntry
                {
                    ProviderMessageSid = message.Sid,
                    Disposition = ReconciliationDisposition.MissingLocally,
                    ProviderStatus = message.Status,
                    ProviderErrorCode = message.ErrorCode,
                    DateSent = message.DateSent
                });
            }
        }

        foreach (var local in localNotifications.Where(n =>
                     n.ProviderMessageSid is null || !matchedSids.Contains(n.ProviderMessageSid)))
        {
            // Messages that were never sent (still scheduled, or called off before
            // sending) have no sent-date at the provider and legitimately do not
            // appear in its sent-message listing — they are not discrepancies.
            if (local.ProviderStatus is "scheduled" or "canceled" or "cancel-requested")
            {
                continue;
            }

            report.Entries.Add(new ReconciliationEntry
            {
                ProviderMessageSid = local.ProviderMessageSid,
                NotificationId = local.Id,
                Disposition = ReconciliationDisposition.MissingAtProvider,
                ProviderStatus = local.ProviderStatus,
                ProviderErrorCode = local.ProviderErrorCode,
                RecordedAt = local.CreatedAt
            });
        }

        report.MatchedCount = report.Entries.Count(e => e.Disposition == ReconciliationDisposition.Matched);
        report.MissingLocallyCount = report.Entries.Count(e => e.Disposition == ReconciliationDisposition.MissingLocally);
        report.MissingAtProviderCount = report.Entries.Count(e => e.Disposition == ReconciliationDisposition.MissingAtProvider);
        return report;
    }

    private async Task SendToShopperAsync(Order order, NotificationType type, string body,
        DateTimeOffset? sendAtUtc, CancellationToken cancellationToken)
    {
        var contactNumbers = await _contactNumberRepository.ListAsync(
            new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);

        foreach (var contactNumber in contactNumbers)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, contactNumber.PhoneNumber,
                body, type, sendAtUtc);
            await SendAndRecordAsync(notification, sendAtUtc, cancellationToken);
        }
    }

    private async Task SendAndRecordAsync(OrderNotification notification, DateTimeOffset? sendAtUtc, CancellationToken cancellationToken)
    {
        try
        {
            var message = await _messagingClient.SendMessageAsync(notification.ToNumber, notification.Body!, sendAtUtc, cancellationToken);
            notification.MarkAccepted(message.Sid, message.Status);
        }
        catch (Exception ex)
        {
            // A message that cannot be sent must never fail the underlying operation.
            var errorCode = (ex as TwilioApiException)?.ErrorCode;
            _logger.LogWarning("Notification of type {NotificationType} for order {OrderId} could not be sent (provider error {ErrorCode}): {Error}",
                notification.Type, notification.OrderId, errorCode?.ToString() ?? "n/a", ex.Message);
            notification.MarkFailed(errorCode, ex.Message);
        }

        await _notificationRepository.AddAsync(notification, cancellationToken);
    }
}
