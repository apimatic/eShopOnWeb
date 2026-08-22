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

public class NotificationOperatorService : INotificationOperatorService
{
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<NotificationResendRecord> _resendRecords;
    private readonly IRepository<Order> _orders;
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly ITwilioMessagingClient _messagingClient;
    private readonly TwilioSettings _twilioSettings;
    private readonly IAppLogger<NotificationOperatorService> _logger;

    public NotificationOperatorService(
        IRepository<OrderNotification> notifications,
        IRepository<NotificationResendRecord> resendRecords,
        IRepository<Order> orders,
        IRepository<ShopperContactNumber> contactNumbers,
        ITwilioMessagingClient messagingClient,
        TwilioSettings twilioSettings,
        IAppLogger<NotificationOperatorService> logger)
    {
        _notifications = notifications;
        _resendRecords = resendRecords;
        _orders = orders;
        _contactNumbers = contactNumbers;
        _messagingClient = messagingClient;
        _twilioSettings = twilioSettings;
        _logger = logger;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new OrderFulfillmentException("An idempotency key is required.");
        }

        var existingAttempt = await _resendRecords.FirstOrDefaultAsync(
            new ResendRecordByKeySpec(notificationId, idempotencyKey.Trim()), cancellationToken);
        if (existingAttempt != null)
        {
            var existingNotification = await _notifications.GetByIdAsync(existingAttempt.ResultNotificationId, cancellationToken);
            if (existingNotification != null)
            {
                return existingNotification;
            }
        }

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (source is null)
        {
            throw new KeyNotFoundException("Notification was not found.");
        }

        var order = await _orders.GetByIdAsync(source.OrderId, cancellationToken);
        if (order is null)
        {
            throw new KeyNotFoundException("Order was not found.");
        }

        if (!source.ContactNumberId.HasValue)
        {
            throw new OrderFulfillmentException("The original destination number is no longer on file and cannot be used.");
        }

        var contact = await _contactNumbers.GetByIdAsync(source.ContactNumberId.Value, cancellationToken);
        if (contact is null)
        {
            throw new OrderFulfillmentException("The original destination number is no longer on file and cannot be used.");
        }

        var destination = contact.CanonicalNumber;

        var body = source.ResolveBodyForResend(order.Id);
        var resent = new OrderNotification(
            order.Id,
            order.BuyerId,
            source.ContactNumberId,
            destination,
            source.Kind,
            body,
            scheduledFor: null,
            sourceNotificationId: source.Id);

        await _notifications.AddAsync(resent, cancellationToken);

        try
        {
            var created = await _messagingClient.CreateMessageAsync(new TwilioCreateMessageRequest
            {
                To = destination,
                Body = body,
                From = _twilioSettings.FromNumber,
                MessagingServiceSid = _twilioSettings.MessagingServiceSid
            }, cancellationToken);

            if (string.IsNullOrEmpty(created.Sid) || string.IsNullOrEmpty(created.Status))
            {
                resent.RecordLocalSendFailure("The provider did not return a message identifier.");
            }
            else
            {
                resent.RecordProviderAccepted(created.Sid, created.Status, created.ErrorCode, created.ErrorMessage);
            }

            await _notifications.UpdateAsync(resent, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Failed to resend notification {SourceNotificationId} as {NotificationId}: {Message}",
                source.Id,
                resent.Id,
                PiiRedactor.Redact(ex.Message));
            resent.RecordLocalSendFailure("The provider rejected or did not accept the message.");
            await _notifications.UpdateAsync(resent, cancellationToken);
        }

        await _resendRecords.AddAsync(new NotificationResendRecord(source.Id, idempotencyKey.Trim(), resent.Id), cancellationToken);
        return resent;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            throw new KeyNotFoundException("Notification was not found.");
        }

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            try
            {
                var updated = await _messagingClient.UpdateMessageAsync(
                    notification.ProviderMessageSid,
                    new TwilioUpdateMessageRequest { Body = string.Empty },
                    cancellationToken);
                notification.ApplyProviderState(
                    updated.Status ?? notification.ProviderStatus,
                    updated.ErrorCode,
                    updated.ErrorMessage,
                    updated.Body ?? string.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Failed to redact provider content for notification {NotificationId}: {Message}",
                    notification.Id,
                    PiiRedactor.Redact(ex.Message));
                throw new OrderFulfillmentException("The provider could not dispose of the message content.");
            }
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new OrderFulfillmentException("`to` must be on or after `from`.");
        }

        var providerMessages = await _messagingClient.ListMessagesFromAsync(
            _twilioSettings.FromNumber,
            from,
            to,
            cancellationToken);

        var providerSids = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .Select(m => m.Sid!)
            .ToList();

        var matchedLocals = providerSids.Count == 0
            ? new List<OrderNotification>()
            : await _notifications.ListAsync(new NotificationsByProviderSidsSpec(providerSids), cancellationToken);

        var localsBySid = matchedLocals
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .ToDictionary(n => n.ProviderMessageSid!, n => n, StringComparer.Ordinal);

        var matched = new List<ReconciliationMatch>();
        var providerOnly = new List<ProviderOnlyMessage>();

        foreach (var message in providerMessages)
        {
            if (string.IsNullOrEmpty(message.Sid))
            {
                continue;
            }

            if (localsBySid.TryGetValue(message.Sid, out var local))
            {
                matched.Add(new ReconciliationMatch
                {
                    NotificationId = local.Id,
                    OrderId = local.OrderId,
                    ProviderMessageSid = message.Sid,
                    LocalStatus = local.ProviderStatus,
                    ProviderStatus = message.Status ?? string.Empty
                });
            }
            else
            {
                providerOnly.Add(new ProviderOnlyMessage
                {
                    ProviderMessageSid = message.Sid,
                    Status = message.Status,
                    Direction = message.Direction,
                    DateSent = message.DateSent
                });
            }
        }

        var localInRange = await _notifications.ListAsync(new NotificationsInCreatedRangeSpec(from, to), cancellationToken);
        var providerSidSet = new HashSet<string>(providerSids, StringComparer.Ordinal);
        var localOnly = localInRange
            .Where(n => string.IsNullOrEmpty(n.ProviderMessageSid) || !providerSidSet.Contains(n.ProviderMessageSid))
            .Select(n => new LocalOnlyNotification
            {
                NotificationId = n.Id,
                OrderId = n.OrderId,
                ProviderMessageSid = n.ProviderMessageSid,
                LocalStatus = n.ProviderStatus
            })
            .ToList();

        return new NotificationReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = _twilioSettings.FromNumber,
            Matched = matched,
            ProviderOnly = providerOnly,
            LocalOnly = localOnly
        };
    }
}
