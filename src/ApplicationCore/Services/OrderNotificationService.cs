using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.ApplicationCore;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<NotificationResendRecord> _resendRecords;
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly ITwilioMessagingClient _messaging;
    private readonly TwilioSettings _twilioSettings;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notifications,
        IRepository<NotificationResendRecord> resendRecords,
        IRepository<ShopperContactNumber> contactNumbers,
        ITwilioMessagingClient messaging,
        TwilioSettings twilioSettings,
        IAppLogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _resendRecords = resendRecords;
        _contactNumbers = contactNumbers;
        _messaging = messaging;
        _twilioSettings = twilioSettings;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
        => NotifyAsync(orderId, buyerId, NotificationKind.OrderPlaced, cancellationToken);

    public async Task NotifyOrderDispatchedAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        await NotifyAsync(orderId, buyerId, NotificationKind.OrderDispatched, cancellationToken);
        await NotifyAsync(orderId, buyerId, NotificationKind.DeliveryFollowUp, cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        await CancelPendingFollowUpsAsync(orderId, cancellationToken);
        await NotifyAsync(orderId, buyerId, NotificationKind.OrderCancelled, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> GetForOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notifications.ListAsync(new NotificationsByOrderIdSpecification(orderId), cancellationToken);
        if (notifications.Count > 0 && notifications.Any(n => n.BuyerId != buyerId))
        {
            throw new NotFoundException("Order was not found.");
        }

        await SyncWithProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>> GetForOrdersAsync(IReadOnlyCollection<int> orderIds, CancellationToken cancellationToken = default)
    {
        if (orderIds.Count == 0)
        {
            return Array.Empty<OrderNotification>();
        }

        var notifications = await _notifications.ListAsync(new NotificationsByOrderIdsSpecification(orderIds), cancellationToken);
        await SyncWithProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.");
        }

        var existing = await _resendRecords.FirstOrDefaultAsync(
            new ResendRecordBySourceAndKeySpecification(notificationId, idempotencyKey),
            cancellationToken);
        if (existing is not null)
        {
            var previous = await _notifications.GetByIdAsync(existing.ResultNotificationId, cancellationToken)
                ?? throw new NotFoundException("Notification was not found.");
            await SyncWithProviderAsync(new[] { previous }, cancellationToken);
            return previous;
        }

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotFoundException("Notification was not found.");

        await SyncWithProviderAsync(new[] { source }, cancellationToken);

        if (!source.DidNotReachShopper())
        {
            throw new ConflictException("Only messages that did not reach the shopper can be re-sent.");
        }

        var body = BuildBody(source.Kind == NotificationKind.Resend
            ? InferOriginalKind(source)
            : source.Kind, source.OrderId);
        var resent = new OrderNotification(source.OrderId, source.BuyerId, NotificationKind.Resend, source.Destination, body);
        resent.MarkAsResendOf(source.Id);
        await _notifications.AddAsync(resent, cancellationToken);

        await SendAsync(resent, sendAt: null, cancellationToken);

        var record = new NotificationResendRecord(source.Id, idempotencyKey, resent.Id);
        await _resendRecords.AddAsync(record, cancellationToken);
        return resent;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotFoundException("Notification was not found.");

        if (!string.IsNullOrWhiteSpace(notification.ProviderSid))
        {
            try
            {
                var result = await _messaging.RedactMessageBodyAsync(notification.ProviderSid, cancellationToken);
                notification.ApplyProviderResult(result.Sid, result.Status, result.ErrorCode);
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to redact provider content for notification {NotificationId}: {Reason}", notification.Id, ex.GetType().Name);
                throw;
            }
        }

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Redacted content for notification {NotificationId}", notification.Id);
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The reconciliation range is invalid.");
        }

        if (string.IsNullOrWhiteSpace(_twilioSettings.FromNumber))
        {
            throw new InvalidOperationException("Twilio:FromNumber is not configured.");
        }

        var providerMessages = await _messaging.ListMessagesFromAsync(new TwilioMessageListRequest
        {
            From = _twilioSettings.FromNumber,
            DateSentAfter = from,
            DateSentBefore = to
        }, cancellationToken);

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrWhiteSpace(m.Sid))
            .GroupBy(m => m.Sid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var providerSids = providerBySid.Keys.ToList();
        var localBySid = new Dictionary<string, OrderNotification>(StringComparer.Ordinal);
        if (providerSids.Count > 0)
        {
            var matchedLocal = await _notifications.ListAsync(new NotificationsByProviderSidsSpecification(providerSids), cancellationToken);
            foreach (var notification in matchedLocal)
            {
                if (!string.IsNullOrWhiteSpace(notification.ProviderSid))
                {
                    localBySid[notification.ProviderSid] = notification;
                }
            }
        }

        var localInRange = await _notifications.ListAsync(new NotificationsInCreatedRangeSpecification(from, to), cancellationToken);

        var matched = new List<ReconciledNotification>();
        var providerOnly = new List<ProviderOnlyMessage>();
        var applicationOnly = new List<ApplicationOnlyNotification>();

        foreach (var provider in providerBySid.Values)
        {
            if (localBySid.TryGetValue(provider.Sid!, out var local))
            {
                matched.Add(new ReconciledNotification
                {
                    NotificationId = local.Id,
                    ProviderSid = provider.Sid!,
                    ApplicationStatus = local.ProviderStatus,
                    ProviderStatus = provider.Status
                });
            }
            else
            {
                providerOnly.Add(new ProviderOnlyMessage
                {
                    ProviderSid = provider.Sid!,
                    ProviderStatus = provider.Status,
                    DateSent = provider.DateSent,
                    DateCreated = provider.DateCreated
                });
            }
        }

        var seenSids = new HashSet<string>(providerBySid.Keys, StringComparer.Ordinal);
        foreach (var local in localInRange)
        {
            if (!string.IsNullOrWhiteSpace(local.ProviderSid) && seenSids.Contains(local.ProviderSid))
            {
                continue;
            }

            applicationOnly.Add(new ApplicationOnlyNotification
            {
                NotificationId = local.Id,
                ProviderSid = local.ProviderSid,
                ApplicationStatus = local.ProviderStatus
            });
        }

        return new NotificationReconciliationReport
        {
            From = from,
            To = to,
            SendingNumber = _twilioSettings.FromNumber,
            Matched = matched,
            ProviderOnly = providerOnly,
            ApplicationOnly = applicationOnly
        };
    }

    private async Task NotifyAsync(int orderId, string buyerId, NotificationKind kind, CancellationToken cancellationToken)
    {
        var destinations = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        if (destinations.Count == 0)
        {
            _logger.LogInformation("Skipping {Kind} notification for order {OrderId}; no contact number on file", kind, orderId);
            return;
        }

        var body = BuildBody(kind, orderId);
        DateTimeOffset? sendAt = kind == NotificationKind.DeliveryFollowUp
            ? DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay)
            : null;

        foreach (var destination in destinations)
        {
            var notification = new OrderNotification(orderId, buyerId, kind, destination.PhoneNumber, body, sendAt);
            try
            {
                await _notifications.AddAsync(notification, cancellationToken);
                await SendAsync(notification, sendAt, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to persist or send {Kind} notification for order {OrderId}: {Reason}", kind, orderId, ex.GetType().Name);
            }
        }
    }

    private async Task SendAsync(OrderNotification notification, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _messaging.CreateMessageAsync(new CreateTwilioMessageRequest
            {
                To = notification.Destination,
                Body = notification.Body ?? string.Empty,
                SendAt = sendAt
            }, cancellationToken);

            notification.ApplyProviderResult(result.Sid, result.Status, result.ErrorCode);
            await _notifications.UpdateAsync(notification, cancellationToken);
            _logger.LogInformation(
                "Sent notification {NotificationId} for order {OrderId} as provider message {ProviderSid} with status {Status}",
                notification.Id, notification.OrderId, result.Sid ?? string.Empty, result.Status ?? string.Empty);
        }
        catch (Exception ex)
        {
            notification.MarkSendFailed("The provider did not accept the message.");
            try
            {
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception updateEx)
            {
                _logger.LogError("Failed to record send failure for notification {NotificationId}: {Reason}", notification.Id, updateEx.GetType().Name);
            }

            _logger.LogError("Provider send failed for notification {NotificationId}: {Reason}", notification.Id, ex.GetType().Name);
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notifications.ListAsync(new PendingFollowUpsByOrderSpecification(orderId), cancellationToken);
        await SyncWithProviderAsync(followUps, cancellationToken);

        foreach (var followUp in followUps.Where(f => f.IsPendingWithProvider() && !string.IsNullOrWhiteSpace(f.ProviderSid)))
        {
            try
            {
                var result = await _messaging.CancelMessageAsync(followUp.ProviderSid!, cancellationToken);
                followUp.ApplyProviderResult(result.Sid, result.Status, result.ErrorCode);
                await _notifications.UpdateAsync(followUp, cancellationToken);
                _logger.LogInformation(
                    "Cancelled follow-up notification {NotificationId} (provider {ProviderSid}) for order {OrderId}",
                    followUp.Id, followUp.ProviderSid ?? string.Empty, orderId);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    "Failed to cancel follow-up notification {NotificationId} for order {OrderId}: {Reason}",
                    followUp.Id, orderId, ex.GetType().Name);
            }
        }
    }

    private async Task SyncWithProviderAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrWhiteSpace(notification.ProviderSid))
            {
                continue;
            }

            try
            {
                var result = await _messaging.FetchMessageAsync(notification.ProviderSid, cancellationToken);
                notification.ApplyProviderResult(result.Sid, result.Status, result.ErrorCode);
                if (notification.ContentRedacted)
                {
                    notification.RedactContent();
                }
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Could not refresh provider status for notification {NotificationId}: {Reason}",
                    notification.Id, ex.GetType().Name);
            }
        }
    }

    private static string BuildBody(NotificationKind kind, int orderId) => kind switch
    {
        NotificationKind.OrderPlaced => $"eShopOnWeb: Your order #{orderId} has been placed. Thank you!",
        NotificationKind.OrderDispatched => $"eShopOnWeb: Order #{orderId} is on its way.",
        NotificationKind.DeliveryFollowUp => $"eShopOnWeb: How did the delivery of order #{orderId} go? We would love your feedback.",
        NotificationKind.OrderCancelled => $"eShopOnWeb: Order #{orderId} has been cancelled.",
        NotificationKind.Resend => $"eShopOnWeb: Here is an update about your order #{orderId}.",
        _ => $"eShopOnWeb: An update about order #{orderId}."
    };

    private static NotificationKind InferOriginalKind(OrderNotification notification)
    {
        return notification.Kind == NotificationKind.Resend
            ? NotificationKind.OrderPlaced
            : notification.Kind;
    }
}
