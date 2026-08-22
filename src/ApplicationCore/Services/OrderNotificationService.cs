using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<NotificationResendRecord> _resendRecords;
    private readonly IContactNumberService _contactNumbers;
    private readonly IMessagingGateway _messaging;
    private readonly IMessagingSettings _messagingSettings;
    private readonly IAppLogger<OrderNotificationService> _logger;
    private readonly TimeProvider _clock;

    public OrderNotificationService(
        IRepository<OrderNotification> notifications,
        IRepository<NotificationResendRecord> resendRecords,
        IContactNumberService contactNumbers,
        IMessagingGateway messaging,
        IMessagingSettings messagingSettings,
        IAppLogger<OrderNotificationService> logger,
        TimeProvider clock)
    {
        _notifications = notifications;
        _resendRecords = resendRecords;
        _contactNumbers = contactNumbers;
        _messaging = messaging;
        _messagingSettings = messagingSettings;
        _logger = logger;
        _clock = clock;
    }

    public Task NotifyOrderPlacedAsync(OrderNotificationDispatchContext context, CancellationToken cancellationToken = default)
    {
        return TrySendAsync(
            context,
            OrderNotificationKind.OrderPlaced,
            $"eShopOnWeb: order {context.OrderId} has been placed. We'll text you when it ships.",
            sendAt: null,
            cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(OrderNotificationDispatchContext context, CancellationToken cancellationToken = default)
    {
        await TrySendAsync(
            context,
            OrderNotificationKind.OrderDispatched,
            $"eShopOnWeb: order {context.OrderId} is on its way.",
            sendAt: null,
            cancellationToken);

        var sendAt = _clock.GetUtcNow().Add(DeliveryFollowUpDelay);
        await TrySendAsync(
            context,
            OrderNotificationKind.DeliveryFollowUp,
            $"eShopOnWeb: how did delivery of order {context.OrderId} go?",
            sendAt,
            cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(OrderNotificationDispatchContext context, CancellationToken cancellationToken = default)
    {
        await CancelPendingFollowUpsAsync(context.OrderId, cancellationToken);

        await TrySendAsync(
            context,
            OrderNotificationKind.OrderCancelled,
            $"eShopOnWeb: order {context.OrderId} has been cancelled.",
            sendAt: null,
            cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, bool refreshFromProvider, CancellationToken cancellationToken = default)
    {
        var notifications = await _notifications.ListAsync(new NotificationsByOrderIdSpec(orderId), cancellationToken);
        if (refreshFromProvider)
        {
            await RefreshFromProviderAsync(notifications, cancellationToken);
        }

        return notifications;
    }

    public Task<OrderNotification?> GetByIdAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        return _notifications.GetByIdAsync(notificationId, cancellationToken);
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new NotificationActionException("An idempotency key is required.");
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original == null)
        {
            throw new EntityNotFoundException("Notification not found.");
        }

        var existing = await _resendRecords.FirstOrDefaultAsync(new ResendRecordByKeySpec(original.Id, idempotencyKey.Trim()), cancellationToken);
        if (existing != null)
        {
            var previous = await _notifications.GetByIdAsync(existing.ResultNotificationId, cancellationToken);
            if (previous != null)
            {
                return previous;
            }
        }

        if (original.ContentRedacted || string.IsNullOrWhiteSpace(original.Body))
        {
            throw new NotificationActionException("The message content has been disposed of and cannot be re-sent.");
        }

        await RefreshFromProviderAsync(new[] { original }, cancellationToken);

        if (original.HasReachedShopper())
        {
            throw new NotificationActionException("The message already reached the shopper.");
        }

        if (string.Equals(original.ProviderStatus, "scheduled", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotificationActionException("A scheduled message that has not yet been sent cannot be re-sent.");
        }

        var stillRegistered = await _contactNumbers.IsStillRegisteredAsync(original.BuyerId, original.DestinationPhoneNumber, cancellationToken);
        if (!stillRegistered)
        {
            throw new NotificationActionException("The destination number is no longer on file for this shopper.");
        }

        var resent = await SendAndStoreAsync(
            original.OrderId,
            original.BuyerId,
            OrderNotificationKind.Resend,
            original.DestinationPhoneNumber,
            original.Body!,
            sendAt: null,
            original.Id,
            cancellationToken);

        await _resendRecords.AddAsync(new NotificationResendRecord(original.Id, idempotencyKey.Trim(), resent.Id), cancellationToken);
        return resent;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification == null)
        {
            throw new EntityNotFoundException("Notification not found.");
        }

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            var updated = await _messaging.UpdateAsync(
                notification.ProviderMessageSid,
                new MessageUpdateRequest { Body = string.Empty },
                cancellationToken);
            notification.ApplyProviderState(updated.Status, updated.ErrorCode, updated.Body);

            var verify = await _messaging.FetchAsync(notification.ProviderMessageSid, cancellationToken);
            if (!string.IsNullOrEmpty(verify.Body))
            {
                throw new NotificationActionException("The provider still has the message content after disposal.");
            }
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new NotificationActionException("The reconciliation range is invalid: 'to' must be on or after 'from'.");
        }

        var fromNumber = _messagingSettings.FromNumber;
        var providerMessages = await _messaging.ListFromSenderAsync(fromNumber, from, to, cancellationToken);
        var providerSids = providerMessages.Select(m => m.Sid).Where(s => !string.IsNullOrEmpty(s)).ToHashSet(StringComparer.Ordinal);

        var localInRange = await _notifications.ListAsync(new NotificationsInCreatedRangeSpec(from, to), cancellationToken);
        IReadOnlyList<OrderNotification> localBySid = providerSids.Count == 0
            ? Array.Empty<OrderNotification>()
            : await _notifications.ListAsync(new NotificationsByProviderSidsSpec(providerSids), cancellationToken);

        var localBySidMap = localBySid.Where(n => n.ProviderMessageSid != null)
            .ToDictionary(n => n.ProviderMessageSid!, StringComparer.Ordinal);

        var matched = new List<ReconciledNotification>();
        var providerOnly = new List<ProviderOnlyNotification>();

        foreach (var message in providerMessages)
        {
            if (localBySidMap.TryGetValue(message.Sid, out var local))
            {
                matched.Add(new ReconciledNotification
                {
                    NotificationId = local.Id,
                    ProviderMessageSid = message.Sid,
                    ProviderStatus = message.Status,
                    ApplicationStatus = local.ProviderStatus,
                    Kind = local.Kind.ToString()
                });
            }
            else
            {
                providerOnly.Add(new ProviderOnlyNotification
                {
                    ProviderMessageSid = message.Sid,
                    Status = message.Status,
                    DateSent = message.DateSent,
                    DateCreated = message.DateCreated
                });
            }
        }

        var matchedSids = matched.Select(m => m.ProviderMessageSid).ToHashSet(StringComparer.Ordinal);
        var applicationOnly = localInRange
            .Where(n => string.IsNullOrEmpty(n.ProviderMessageSid) || !providerSids.Contains(n.ProviderMessageSid))
            .Where(n => n.ProviderMessageSid == null || !matchedSids.Contains(n.ProviderMessageSid))
            .Select(n => new ApplicationOnlyNotification
            {
                NotificationId = n.Id,
                ProviderMessageSid = n.ProviderMessageSid,
                Status = n.ProviderStatus,
                Kind = n.Kind.ToString()
            })
            .ToList();

        return new NotificationReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = fromNumber,
            Matched = matched,
            ProviderOnly = providerOnly,
            ApplicationOnly = applicationOnly
        };
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _notifications.ListAsync(new NotificationsByOrderIdSpec(orderId), cancellationToken);
        foreach (var notification in notifications.Where(n => n.IsScheduledFollowUpStillPending()))
        {
            try
            {
                var updated = await _messaging.UpdateAsync(
                    notification.ProviderMessageSid!,
                    new MessageUpdateRequest { Status = "canceled" },
                    cancellationToken);
                notification.ApplyProviderState(updated.Status, updated.ErrorCode, updated.Body);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cancel scheduled follow-up notification {NotificationId} sid {Sid} for order {OrderId}", notification.Id, notification.ProviderMessageSid ?? string.Empty, orderId);
            }
        }
    }

    private async Task TrySendAsync(
        OrderNotificationDispatchContext context,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var destination = await _contactNumbers.GetPrimaryForBuyerAsync(context.BuyerId, cancellationToken);
            if (destination == null)
            {
                _logger.LogInformation("Skipping {Kind} SMS for order {OrderId}; shopper has no number on file.", kind, context.OrderId);
                return;
            }

            await SendAndStoreAsync(context.OrderId, context.BuyerId, kind, destination.PhoneNumber, body, sendAt, originalNotificationId: null, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMS notification {Kind} for order {OrderId} failed; the order operation still succeeded.", kind, context.OrderId);
        }
    }

    private async Task<OrderNotification> SendAndStoreAsync(
        int orderId,
        string buyerId,
        OrderNotificationKind kind,
        string destinationPhoneNumber,
        string body,
        DateTimeOffset? sendAt,
        int? originalNotificationId,
        CancellationToken cancellationToken)
    {
        string? sid = null;
        var status = "failed";
        int? errorCode = null;

        try
        {
            var sent = await _messaging.SendAsync(new OutboundMessageRequest
            {
                To = destinationPhoneNumber,
                Body = body,
                SendAt = sendAt
            }, cancellationToken);

            sid = sent.Sid;
            status = sent.Status;
            errorCode = sent.ErrorCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Provider rejected {Kind} SMS for order {OrderId}.", kind, orderId);
            status = "failed";
        }

        var notification = new OrderNotification(
            orderId,
            buyerId,
            kind,
            destinationPhoneNumber,
            body,
            sid,
            status,
            errorCode,
            sendAt,
            originalNotificationId);

        return await _notifications.AddAsync(notification, cancellationToken);
    }

    private async Task RefreshFromProviderAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid) || notification.IsTerminalStatus())
            {
                continue;
            }

            try
            {
                var current = await _messaging.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                notification.ApplyProviderState(current.Status, current.ErrorCode, notification.ContentRedacted ? null : current.Body);
                if (!notification.ContentRedacted && string.IsNullOrEmpty(current.Body))
                {
                    notification.MarkContentRedacted();
                }
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not refresh provider status for notification {NotificationId} sid {Sid}", notification.Id, notification.ProviderMessageSid ?? string.Empty);
            }
        }
    }
}
