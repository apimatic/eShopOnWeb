using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<ResendIdempotencyRecord> _resendKeys;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ISmsGateway _sms;
    private readonly IMessagingSettings _messagingSettings;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notifications,
        IRepository<ResendIdempotencyRecord> resendKeys,
        IRepository<ContactNumber> contactNumbers,
        ISmsGateway sms,
        IMessagingSettings messagingSettings,
        IAppLogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _resendKeys = resendKeys;
        _contactNumbers = contactNumbers;
        _sms = sms;
        _messagingSettings = messagingSettings;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(int orderId, string buyerId, CancellationToken cancellationToken) =>
        SendToBuyerNumbersAsync(
            orderId,
            buyerId,
            NotificationKind.OrderPlaced,
            $"Your eShopOnWeb order #{orderId} has been placed.",
            scheduleFollowUp: false,
            cancellationToken);

    public Task NotifyOrderDispatchedAsync(int orderId, string buyerId, CancellationToken cancellationToken) =>
        SendToBuyerNumbersAsync(
            orderId,
            buyerId,
            NotificationKind.OrderDispatched,
            $"Your eShopOnWeb order #{orderId} is on its way.",
            scheduleFollowUp: true,
            cancellationToken);

    public Task NotifyOrderCancelledAsync(int orderId, string buyerId, CancellationToken cancellationToken) =>
        SendToBuyerNumbersAsync(
            orderId,
            buyerId,
            NotificationKind.OrderCancelled,
            $"Your eShopOnWeb order #{orderId} has been cancelled.",
            scheduleFollowUp: false,
            cancellationToken);

    public async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notifications.ListAsync(new FollowUpsByOrderIdSpec(orderId), cancellationToken);
        foreach (var followUp in followUps.Where(n => n.IsCancellableFollowUp()))
        {
            await CancelFollowUpAsync(followUp, cancellationToken);
        }
    }

    public async Task CancelPendingFollowUpsForNumberAsync(string buyerId, string canonicalNumber, CancellationToken cancellationToken)
    {
        var followUps = await _notifications.ListAsync(
            new FollowUpsByBuyerAndNumberSpec(buyerId, canonicalNumber), cancellationToken);
        foreach (var followUp in followUps.Where(n => n.IsCancellableFollowUp()))
        {
            await CancelFollowUpAsync(followUp, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        return await _notifications.ListAsync(new OrderNotificationsByOrderIdSpec(orderId), cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderRefreshingAsync(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await ListForOrderAsync(orderId, cancellationToken);
        foreach (var notification in notifications.Where(n => !string.IsNullOrEmpty(n.ProviderSid)))
        {
            try
            {
                var snapshot = await _sms.FetchAsync(notification.ProviderSid!, cancellationToken);
                if (snapshot.Succeeded)
                {
                    notification.ApplyProviderSnapshot(
                        snapshot.Status,
                        snapshot.ErrorCode,
                        snapshot.ErrorMessage,
                        snapshot.DateSent,
                        snapshot.Body);
                    if (notification.ContentRedacted)
                    {
                        notification.MarkContentRedacted();
                    }
                    await _notifications.UpdateAsync(notification, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Failed to refresh provider status for notification {NotificationId}: {Message}",
                    notification.Id,
                    ex.Message);
            }
        }

        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        var existingKey = await _resendKeys.FirstOrDefaultAsync(
            new ResendIdempotencySpec(notificationId, idempotencyKey.Trim()), cancellationToken);
        if (existingKey is not null)
        {
            var existing = await _notifications.GetByIdAsync(existingKey.ResultNotificationId, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }
        }

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException("Notification not found.");

        var stillRegistered = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpec(source.BuyerId, source.DestinationNumber), cancellationToken);
        if (stillRegistered is null)
        {
            throw new InvalidOperationException("The destination is no longer registered; the message will not be sent again.");
        }

        var body = source.ContentRedacted
            ? $"Your eShopOnWeb order #{source.OrderId} (follow-up)."
            : source.Body ?? $"Your eShopOnWeb order #{source.OrderId}.";

        var resent = new OrderNotification(
            source.OrderId,
            source.BuyerId,
            NotificationKind.Resend,
            source.DestinationNumber,
            body,
            parentNotificationId: source.Id);

        await DispatchAndPersistAsync(resent, schedule: false, sendAt: null, cancellationToken);

        var record = new ResendIdempotencyRecord(notificationId, idempotencyKey.Trim(), resent.Id);
        await _resendKeys.AddAsync(record, cancellationToken);
        return resent;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException("Notification not found.");

        if (!string.IsNullOrEmpty(notification.ProviderSid))
        {
            var snapshot = await _sms.RedactContentAsync(notification.ProviderSid, cancellationToken);
            if (!snapshot.Succeeded)
            {
                throw new Exceptions.MessagingProviderException(
                    snapshot.FailureMessage ?? "The provider could not dispose of the message content.");
            }

            var originalGone = snapshot.Body is null
                || snapshot.Body.Length == 0
                || notification.Body is null
                || snapshot.Body != notification.Body;

            if (!originalGone)
            {
                throw new Exceptions.MessagingProviderException(
                    "The provider still returns the original message text after content disposal.");
            }

            notification.ApplyProviderSnapshot(
                snapshot.Status,
                snapshot.ErrorCode,
                snapshot.ErrorMessage,
                snapshot.DateSent,
                body: null);
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var list = await _sms.ListSentFromConfiguredNumberAsync(from, to, cancellationToken);
        if (!list.Succeeded)
        {
            throw new Exceptions.MessagingProviderException(
                list.FailureMessage ?? "The provider could not list messages for reconciliation.");
        }

        var local = await _notifications.ListAsync(new NotificationsCreatedBetweenSpec(from, to), cancellationToken);
        var localBySid = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderSid))
            .GroupBy(n => n.ProviderSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matched = new List<ReconciledMessage>();
        var providerOnly = new List<ReconciledMessage>();
        var seenSids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var message in list.Messages)
        {
            if (string.IsNullOrEmpty(message.Sid))
            {
                continue;
            }

            seenSids.Add(message.Sid);
            if (localBySid.TryGetValue(message.Sid, out var localNote))
            {
                matched.Add(new ReconciledMessage(localNote.Id, message.Sid, message.Status, message.DateSent));
            }
            else
            {
                providerOnly.Add(new ReconciledMessage(null, message.Sid, message.Status, message.DateSent));
            }
        }

        var eshopOnly = local
            .Where(n => string.IsNullOrEmpty(n.ProviderSid) || !seenSids.Contains(n.ProviderSid))
            .Select(n => new ReconciledMessage(n.Id, n.ProviderSid, n.ProviderStatus, n.DateSent))
            .ToList();

        return new NotificationReconciliationReport(
            from,
            to,
            _messagingSettings.FromNumber,
            list.Truncated,
            matched,
            providerOnly,
            eshopOnly);
    }

    private async Task SendToBuyerNumbersAsync(
        int orderId,
        string buyerId,
        NotificationKind kind,
        string body,
        bool scheduleFollowUp,
        CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpec(buyerId), cancellationToken);
        if (numbers.Count == 0)
        {
            _logger.LogInformation("No contact number on file for order {OrderId}; skipping SMS.", orderId);
            return;
        }

        foreach (var number in numbers)
        {
            var notification = new OrderNotification(orderId, buyerId, kind, number.CanonicalNumber, body);
            await DispatchAndPersistAsync(notification, schedule: false, sendAt: null, cancellationToken);

            if (!scheduleFollowUp)
            {
                continue;
            }

            var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
            var followUp = new OrderNotification(
                orderId,
                buyerId,
                NotificationKind.DeliveryFollowUp,
                number.CanonicalNumber,
                $"How did the delivery of eShopOnWeb order #{orderId} go?");
            followUp.MarkScheduledFor(sendAt);
            await DispatchAndPersistAsync(followUp, schedule: true, sendAt: sendAt, cancellationToken);
        }
    }

    private async Task DispatchAndPersistAsync(
        OrderNotification notification,
        bool schedule,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        SmsDispatchResult result;
        try
        {
            result = schedule
                ? await _sms.ScheduleAsync(notification.DestinationNumber, notification.Body ?? string.Empty, sendAt!.Value, cancellationToken)
                : await _sms.SendAsync(notification.DestinationNumber, notification.Body ?? string.Empty, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "SMS dispatch threw for notification kind {Kind} on order {OrderId}: {Message}",
                notification.Kind,
                notification.OrderId,
                ex.Message);
            notification.RecordFailure("failed", null, "The messaging provider did not accept the message.");
            await _notifications.AddAsync(notification, cancellationToken);
            return;
        }

        if (result.Accepted)
        {
            notification.RecordAccepted(result.ProviderSid, result.Status, result.DateSent);
        }
        else
        {
            notification.RecordFailure(result.Status, result.ErrorCode, result.ErrorMessage);
        }

        await _notifications.AddAsync(notification, cancellationToken);
        _logger.LogInformation(
            "Recorded notification {NotificationId} kind {Kind} for order {OrderId} accepted={Accepted} sidPresent={SidPresent}",
            notification.Id,
            notification.Kind,
            notification.OrderId,
            result.Accepted,
            !string.IsNullOrEmpty(result.ProviderSid));
    }

    private async Task CancelFollowUpAsync(OrderNotification followUp, CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _sms.CancelScheduledAsync(followUp.ProviderSid!, cancellationToken);
            if (snapshot.Succeeded)
            {
                followUp.ApplyProviderSnapshot(
                    snapshot.Status,
                    snapshot.ErrorCode,
                    snapshot.ErrorMessage,
                    snapshot.DateSent,
                    snapshot.Body);
            }
            else
            {
                followUp.RecordFailure(snapshot.Status, snapshot.ErrorCode, snapshot.FailureMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Failed to cancel follow-up notification {NotificationId}: {Message}",
                followUp.Id,
                ex.Message);
            followUp.RecordFailure(followUp.ProviderStatus, null, "Provider cancel failed.");
        }

        await _notifications.UpdateAsync(followUp, cancellationToken);
    }
}
