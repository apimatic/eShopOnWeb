using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    // The follow-up is scheduled with the provider itself; nothing in this app sends it later.
    private static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly ISmsClient _smsClient;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notificationRepository,
        IRepository<ContactNumber> contactNumberRepository,
        ISmsClient smsClient,
        IAppLogger<OrderNotificationService> logger)
    {
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _smsClient = smsClient;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        return SendToBuyerAsync(order, NotificationType.OrderPlaced,
            $"eShop: your order #{order.Id} was placed. Total ${order.Total():0.00}. We'll text you when it ships.",
            null, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await SendToBuyerAsync(order, NotificationType.OrderDispatched,
            $"eShop: good news - order #{order.Id} is on its way.",
            null, cancellationToken);

        await SendToBuyerAsync(order, NotificationType.DeliveryFollowUp,
            $"eShop: how did the delivery of order #{order.Id} go? We'd love to know.",
            DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay), cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        await SendToBuyerAsync(order, NotificationType.OrderCancelled,
            $"eShop: your order #{order.Id} has been cancelled. Contact support if this is unexpected.",
            null, cancellationToken);

        // Call off any follow-up that has not yet gone out: a cancelled order must
        // never produce a "how was your delivery" message.
        var scheduledSpec = new ScheduledFollowUpsForOrderSpecification(order.Id);
        var scheduled = await _notificationRepository.ListAsync(scheduledSpec, cancellationToken);
        foreach (var followUp in scheduled)
        {
            try
            {
                var cancelled = await _smsClient.CancelScheduledMessageAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.UpdateProviderStatus(cancelled ? "canceled" : followUp.ProviderStatus, followUp.ProviderErrorCode);
                if (!cancelled)
                {
                    _logger.LogWarning("Provider did not confirm cancellation of scheduled message {MessageSid} for order {OrderId}.",
                        followUp.ProviderMessageSid!, order.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cancel scheduled message {MessageSid} for order {OrderId}.", followUp.ProviderMessageSid!, order.Id);
            }

            await _notificationRepository.UpdateAsync(followUp, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var spec = new NotificationsByOrderSpecification(orderId);
        var notifications = await _notificationRepository.ListAsync(spec, cancellationToken);

        foreach (var notification in notifications.Where(n => !n.IsTerminal && n.ProviderMessageSid != null))
        {
            await RefreshFromProviderAsync(notification, cancellationToken);
        }

        return notifications;
    }

    public async Task<ResendNotificationResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original == null)
        {
            return new ResendNotificationResult { Status = ResendStatus.NotFound };
        }

        var keySpec = new NotificationByIdempotencyKeySpecification(notificationId, idempotencyKey);
        var existing = await _notificationRepository.FirstOrDefaultAsync(keySpec, cancellationToken);
        if (existing != null)
        {
            return new ResendNotificationResult { Status = ResendStatus.DuplicateRequest, Notification = existing };
        }

        if (original.ContentRedacted || original.Body == null)
        {
            return new ResendNotificationResult { Status = ResendStatus.ContentRedacted, Notification = original };
        }

        // A deleted contact number must never be sent to again.
        var contactNumber = await _contactNumberRepository.GetByIdAsync(original.ContactNumberId, cancellationToken);
        if (contactNumber == null || contactNumber.BuyerId != original.BuyerId)
        {
            return new ResendNotificationResult { Status = ResendStatus.ContactNumberRemoved, Notification = original };
        }

        var resend = new OrderNotification(original.OrderId, original.BuyerId, original.ContactNumberId,
            original.NotificationType, original.Body, null, idempotencyKey, notificationId);

        var sendResult = await TrySendAsync(contactNumber.PhoneNumber, original.Body, null, cancellationToken);
        ApplySendResult(resend, sendResult);

        resend = await _notificationRepository.AddAsync(resend, cancellationToken);
        return new ResendNotificationResult
        {
            Status = sendResult.Success ? ResendStatus.Resent : ResendStatus.SendFailed,
            Notification = resend
        };
    }

    public async Task<RedactContentResult> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification == null)
        {
            return new RedactContentResult { Status = RedactContentStatus.NotFound };
        }

        if (notification.ContentRedacted)
        {
            return new RedactContentResult { Status = RedactContentStatus.AlreadyRedacted, Notification = notification };
        }

        if (notification.ProviderMessageSid != null)
        {
            bool redacted;
            try
            {
                redacted = await _smsClient.RedactMessageBodyAsync(notification.ProviderMessageSid, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to redact provider message {MessageSid}.", notification.ProviderMessageSid);
                return new RedactContentResult { Status = RedactContentStatus.ProviderRedactionFailed, Notification = notification };
            }

            if (!redacted)
            {
                return new RedactContentResult { Status = RedactContentStatus.ProviderRedactionFailed, Notification = notification };
            }
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        return new RedactContentResult { Status = RedactContentStatus.Redacted, Notification = notification };
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _smsClient.ListMessagesFromSenderAsync(fromUtc, toUtc, cancellationToken);

        var localSpec = new NotificationsCreatedInRangeSpecification(fromUtc, toUtc);
        var localNotifications = await _notificationRepository.ListAsync(localSpec, cancellationToken);

        var localBySid = localNotifications
            .Where(n => n.ProviderMessageSid != null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var report = new ReconciliationReport { FromUtc = fromUtc, ToUtc = toUtc };
        var matched = new List<ReconciliationMatch>();
        var missingFromLocal = new List<SmsMessageState>();
        var matchedSids = new HashSet<string>();

        foreach (var providerMessage in providerMessages)
        {
            if (localBySid.TryGetValue(providerMessage.MessageSid, out var notification))
            {
                matchedSids.Add(providerMessage.MessageSid);
                matched.Add(new ReconciliationMatch
                {
                    Notification = notification,
                    ProviderMessage = providerMessage,
                    StatusMatches = string.Equals(notification.ProviderStatus, providerMessage.Status, StringComparison.OrdinalIgnoreCase)
                });
            }
            else
            {
                missingFromLocal.Add(providerMessage);
            }
        }

        report.Matched = matched;
        report.MissingFromLocal = missingFromLocal;
        report.MissingFromProvider = localBySid.Values
            .Where(n => !matchedSids.Contains(n.ProviderMessageSid!))
            .ToList();
        return report;
    }

    private async Task SendToBuyerAsync(Order order, NotificationType type, string body,
        DateTimeOffset? sendAtUtc, CancellationToken cancellationToken)
    {
        var contactNumbersSpec = new ContactNumbersByBuyerSpecification(order.BuyerId);
        var contactNumbers = await _contactNumberRepository.ListAsync(contactNumbersSpec, cancellationToken);
        if (contactNumbers.Count == 0)
        {
            return; // No number on file: the shopper is simply not messaged.
        }

        foreach (var contactNumber in contactNumbers)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, contactNumber.Id, type, body, sendAtUtc);
            var sendResult = await TrySendAsync(contactNumber.PhoneNumber, body, sendAtUtc, cancellationToken);
            ApplySendResult(notification, sendResult);
            await _notificationRepository.AddAsync(notification, cancellationToken);
        }
    }

    private async Task<SmsSendResult> TrySendAsync(string toE164, string body, DateTimeOffset? sendAtUtc, CancellationToken cancellationToken)
    {
        try
        {
            return await _smsClient.SendMessageAsync(toE164, body, sendAtUtc, cancellationToken);
        }
        catch (Exception ex)
        {
            // Never let a messaging failure fail the underlying operation.
            _logger.LogError(ex, "SMS provider call failed.");
            return new SmsSendResult { Success = false, Status = "failed" };
        }
    }

    private void ApplySendResult(OrderNotification notification, SmsSendResult sendResult)
    {
        if (sendResult.Success && sendResult.MessageSid != null)
        {
            notification.MarkAccepted(sendResult.MessageSid, sendResult.Status ?? "queued");
        }
        else
        {
            _logger.LogWarning("SMS send was rejected by the provider for order {OrderId} (notification type {NotificationType}), error code {ErrorCode}.",
                notification.OrderId, notification.NotificationType, sendResult.ErrorCode?.ToString() ?? "none");
            notification.MarkSendFailed(sendResult.Status, sendResult.ErrorCode);
        }
    }

    private async Task RefreshFromProviderAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var state = await _smsClient.FetchMessageAsync(notification.ProviderMessageSid!, cancellationToken);
            if (state != null)
            {
                notification.UpdateProviderStatus(state.Status, state.ErrorCode);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh status for provider message {MessageSid}.", notification.ProviderMessageSid!);
        }
    }
}
