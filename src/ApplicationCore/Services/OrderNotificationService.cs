using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Extensions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<BuyerContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<NotificationResendKey> _resendKeys;
    private readonly ITrackedNotificationStore _trackedNotifications;
    private readonly ITwilioMessagingClient _twilio;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<BuyerContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IRepository<NotificationResendKey> resendKeys,
        ITrackedNotificationStore trackedNotifications,
        ITwilioMessagingClient twilio,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _resendKeys = resendKeys;
        _trackedNotifications = trackedNotifications;
        _twilio = twilio;
        _logger = logger;
    }

    public Task<IReadOnlyList<OrderNotification>> NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
        => NotifyAsync(order, OrderNotificationKind.OrderPlaced, BuildPlacedBody(order.Id), sendAt: null, cancellationToken);

    public async Task<IReadOnlyList<OrderNotification>> NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var dispatched = await NotifyAsync(order, OrderNotificationKind.OrderDispatched, BuildDispatchedBody(order.Id), sendAt: null, cancellationToken);
        var followUpAt = DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay);
        var followUp = await NotifyAsync(order, OrderNotificationKind.DeliveryFollowUp, BuildFollowUpBody(order.Id), followUpAt, cancellationToken);

        return dispatched.Concat(followUp).ToList();
    }

    public async Task<IReadOnlyList<OrderNotification>> NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        await CancelOutstandingFollowUpsAsync(order.Id, cancellationToken);
        return await NotifyAsync(order, OrderNotificationKind.OrderCancelled, BuildCancelledBody(order.Id), sendAt: null, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, bool refreshFromProvider, CancellationToken cancellationToken = default)
    {
        var notifications = await _notifications.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        if (refreshFromProvider)
        {
            foreach (var notification in notifications)
            {
                await RefreshFromProviderAsync(notification, cancellationToken);
            }
        }

        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        var existingKey = await _resendKeys.FirstOrDefaultAsync(
            new NotificationResendKeySpecification(notificationId, idempotencyKey), cancellationToken);
        if (existingKey?.ResultNotificationId is int existingResultId)
        {
            var existingResult = await _notifications.GetByIdAsync(existingResultId, cancellationToken);
            if (existingResult != null)
            {
                await RefreshFromProviderAsync(existingResult, cancellationToken);
                return existingResult;
            }
        }

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Notification {notificationId} was not found.");

        if (!source.DidNotReachShopper)
        {
            throw new InvalidOperationException("Only messages that did not reach the shopper can be re-sent.");
        }

        if (string.IsNullOrEmpty(source.Body) || source.ContentRedacted)
        {
            throw new InvalidOperationException("The original message content is no longer available to re-send.");
        }

        var stillOnFile = await DestinationStillRegisteredAsync(source, cancellationToken);
        if (!stillOnFile)
        {
            throw new InvalidOperationException("The destination is no longer on file for this shopper.");
        }

        if (existingKey == null)
        {
            existingKey = new NotificationResendKey(notificationId, idempotencyKey);
            existingKey = await _resendKeys.AddAsync(existingKey, cancellationToken);
        }

        var resend = new OrderNotification(
            source.OrderId,
            source.BuyerId,
            source.ContactNumberId,
            source.DestinationPhoneNumber,
            OrderNotificationKind.Resend,
            source.Body);
        resend.MarkAsResendOf(source.Id);
        resend = await _notifications.AddAsync(resend, cancellationToken);

        await SendAndRecordAsync(resend, sendAt: null, cancellationToken);

        existingKey.AssignResult(resend.Id);
        await _resendKeys.UpdateAsync(existingKey, cancellationToken);

        return resend;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _trackedNotifications.GetAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Notification {notificationId} was not found.");

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            try
            {
                var updated = await _twilio.RedactMessageBodyAsync(notification.ProviderMessageSid, cancellationToken);
                notification.SyncFromProvider(updated.Status, updated.ErrorCode, PhoneNumberSanitizer.Redact(updated.ErrorMessage), body: null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    "Failed to redact provider content for notification {NotificationId}: {Message}",
                    notification.Id,
                    PhoneNumberSanitizer.Redact(ex.Message) ?? ex.Message);
                throw;
            }
        }

        notification.MarkContentRedacted();
        await _trackedNotifications.SaveAsync(cancellationToken);
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The 'to' timestamp must be on or after 'from'.");
        }

        var providerPage = await _twilio.ListMessagesFromConfiguredSenderAsync(from, to, cancellationToken);
        var providerMessages = providerPage.Messages;

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var localInRange = (await _notifications.ListAsync(new NotificationsInCreatedRangeSpecification(from, to), cancellationToken)).ToList();
        var localByProviderSid = localInRange
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        if (providerBySid.Count > 0)
        {
            var extraLocal = await _notifications.ListAsync(new NotificationsByProviderSidsSpecification(providerBySid.Keys), cancellationToken);
            foreach (var notification in extraLocal)
            {
                if (!string.IsNullOrEmpty(notification.ProviderMessageSid) && !localByProviderSid.ContainsKey(notification.ProviderMessageSid))
                {
                    localByProviderSid[notification.ProviderMessageSid] = notification;
                    localInRange.Add(notification);
                }
            }
        }

        var matched = new List<ReconciledMessage>();
        var providerOnly = new List<ProviderOnlyMessage>();
        var localOnly = new List<LocalOnlyMessage>();
        var matchedSids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (sid, provider) in providerBySid)
        {
            if (localByProviderSid.TryGetValue(sid, out var local))
            {
                matched.Add(new ReconciledMessage(local.Id, sid, local.ProviderStatus, provider.Status));
                matchedSids.Add(sid);
            }
            else
            {
                providerOnly.Add(new ProviderOnlyMessage(sid, provider.Status, provider.DateSent));
            }
        }

        foreach (var local in localInRange)
        {
            if (!string.IsNullOrEmpty(local.ProviderMessageSid) && matchedSids.Contains(local.ProviderMessageSid))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(local.ProviderMessageSid) && providerBySid.ContainsKey(local.ProviderMessageSid))
            {
                continue;
            }

            localOnly.Add(new LocalOnlyMessage(local.Id, local.ProviderMessageSid, local.ProviderStatus));
        }

        return new NotificationReconciliationReport(from, to, providerPage.FromNumber, matched, providerOnly, localOnly);
    }

    private async Task<IReadOnlyList<OrderNotification>> NotifyAsync(
        Order order,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        var destinations = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
        if (destinations.Count == 0)
        {
            return Array.Empty<OrderNotification>();
        }

        var created = new List<OrderNotification>();
        foreach (var destination in destinations)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, destination.Id, destination.PhoneNumber, kind, body);
            notification = await _notifications.AddAsync(notification, cancellationToken);
            await SendAndRecordAsync(notification, sendAt, cancellationToken);
            created.Add(notification);
        }

        return created;
    }

    private async Task SendAndRecordAsync(OrderNotification notification, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        try
        {
            var created = await _twilio.CreateMessageAsync(
                new CreateProviderMessageRequest(notification.DestinationPhoneNumber, notification.Body ?? string.Empty, sendAt),
                cancellationToken);
            notification.RecordProviderAcceptance(created.Sid, created.Status, sendAt);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                "SMS send failed for notification {NotificationId} kind {Kind} order {OrderId}: {Message}",
                notification.Id,
                notification.Kind,
                notification.OrderId,
                PhoneNumberSanitizer.Redact(ex.Message) ?? ex.Message);
            notification.RecordProviderFailure("failed", errorCode: null, PhoneNumberSanitizer.Redact(ex.Message));
        }

        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    private async Task CancelOutstandingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notifications.ListAsync(new ScheduledFollowUpNotificationsByOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            if (string.IsNullOrEmpty(followUp.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var updated = await _twilio.CancelMessageAsync(followUp.ProviderMessageSid, cancellationToken);
                followUp.SyncFromProvider(updated.Status, updated.ErrorCode, PhoneNumberSanitizer.Redact(updated.ErrorMessage), updated.Body);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    "Failed to cancel scheduled follow-up notification {NotificationId} for order {OrderId}: {Message}",
                    followUp.Id,
                    orderId,
                    PhoneNumberSanitizer.Redact(ex.Message) ?? ex.Message);
            }

            await _notifications.UpdateAsync(followUp, cancellationToken);
        }
    }

    private async Task RefreshFromProviderAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            return;
        }

        try
        {
            var current = await _twilio.FetchMessageAsync(notification.ProviderMessageSid, cancellationToken);
            if (!notification.ContentRedacted && string.IsNullOrEmpty(current.Body))
            {
                notification.MarkContentRedacted();
            }

            var body = notification.ContentRedacted ? null : current.Body;
            notification.SyncFromProvider(current.Status, current.ErrorCode, PhoneNumberSanitizer.Redact(current.ErrorMessage), body);
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                "Failed to refresh provider status for notification {NotificationId}: {Message}",
                notification.Id,
                PhoneNumberSanitizer.Redact(ex.Message) ?? ex.Message);
        }
    }

    private async Task<bool> DestinationStillRegisteredAsync(OrderNotification source, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(source.BuyerId), cancellationToken);
        return numbers.Any(n => n.Id == source.ContactNumberId || n.PhoneNumber == source.DestinationPhoneNumber);
    }

    private static string BuildPlacedBody(int orderId)
        => $"eShop: order {orderId} placed. We will text you when it ships.";

    private static string BuildDispatchedBody(int orderId)
        => $"eShop: order {orderId} is on its way.";

    private static string BuildFollowUpBody(int orderId)
        => $"eShop: how did delivery of order {orderId} go?";

    private static string BuildCancelledBody(int orderId)
        => $"eShop: order {orderId} was cancelled.";
}
