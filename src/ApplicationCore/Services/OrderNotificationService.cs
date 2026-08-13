using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Sms;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Sends the shopper their order updates and lets an operator act over what was sent. Messaging is
/// always best-effort: a send that fails is recorded on an <see cref="OrderNotification"/> but never
/// bubbles out to fail the order operation that triggered it. A shopper's number is never logged.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How far after dispatch the "how did delivery go?" follow-up is queued (a few days).</summary>
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly ISmsGateway _sms;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notificationRepository,
        IRepository<ContactNumber> contactNumberRepository,
        ISmsGateway sms,
        IAppLogger<OrderNotificationService> logger)
    {
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _sms = sms;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShop: your order #{order.Id} has been placed. Thank you for shopping with us!";
        foreach (var contact in await GetContactsAsync(order.BuyerId, cancellationToken))
        {
            await SendImmediateAsync(order, NotificationKind.OrderPlaced, body, contact, cancellationToken);
        }
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var dispatchedBody = $"eShop: good news - your order #{order.Id} is on its way!";
        var followUpBody = $"eShop: how did the delivery of your order #{order.Id} go? We would love your feedback.";
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);

        foreach (var contact in await GetContactsAsync(order.BuyerId, cancellationToken))
        {
            await SendImmediateAsync(order, NotificationKind.OrderDispatched, dispatchedBody, contact, cancellationToken);
            await ScheduleFollowUpAsync(order, followUpBody, contact, sendAt, cancellationToken);
        }
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        // Call off any not-yet-sent follow-up first: a "how did delivery go?" for a cancelled order is
        // exactly the incident this must prevent.
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        var body = $"eShop: your order #{order.Id} has been cancelled. If this is unexpected, please contact support.";
        foreach (var contact in await GetContactsAsync(order.BuyerId, cancellationToken))
        {
            await SendImmediateAsync(order, NotificationKind.OrderCancelled, body, contact, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.ListAsync(
            new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshStatusesAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>> GetNotificationsForOrdersAsync(int[] orderIds, CancellationToken cancellationToken = default)
    {
        if (orderIds.Length == 0)
        {
            return Array.Empty<OrderNotification>();
        }

        var notifications = await _notificationRepository.ListAsync(
            new OrderNotificationsByOrderIdsSpecification(orderIds), cancellationToken);
        await RefreshStatusesAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task RefreshStatusesAsync(IReadOnlyCollection<OrderNotification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var notification in notifications)
        {
            if (notification.ProviderMessageSid is null || notification.IsTerminal)
            {
                continue;
            }

            try
            {
                var message = await _sms.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                if (message is null)
                {
                    continue;
                }

                notification.UpdateProviderState(message.Status ?? notification.ProviderStatus,
                    message.ErrorCode, message.ErrorMessage, message.SentAt);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not refresh delivery state for notification {Id}: {Error}",
                    notification.Id, ex.Message);
            }
        }
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // Idempotency: a repeat under the same key returns the earlier result and sends nothing new.
        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (existing is not null)
        {
            return new ResendResult { Outcome = ResendOutcome.DuplicateIgnored, NotificationId = existing.Id };
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            return new ResendResult { Outcome = ResendOutcome.OriginalNotFound };
        }

        if (original.ContentDisposed || string.IsNullOrEmpty(original.Body))
        {
            return new ResendResult { Outcome = ResendOutcome.ContentDisposed };
        }

        // The number must still be on file: a removed number must never be messaged again.
        var contact = await _contactNumberRepository.GetByIdAsync(original.ContactNumberId, cancellationToken);
        if (contact is null)
        {
            return new ResendResult { Outcome = ResendOutcome.DestinationRemoved };
        }

        // Reserve the idempotency key by persisting the new record before sending, so a concurrent or
        // repeated request under the same key finds it and does not send a second message.
        var resend = new OrderNotification(original.OrderId, original.OwnerId, original.Kind,
            contact.Id, original.Body!, idempotencyKey: idempotencyKey, resendOfNotificationId: original.Id);
        await _notificationRepository.AddAsync(resend, cancellationToken);

        await TrySendAsync(resend, contact.PhoneNumber, cancellationToken);
        await _notificationRepository.UpdateAsync(resend, cancellationToken);

        return new ResendResult { Outcome = ResendOutcome.Sent, NotificationId = resend.Id };
    }

    public async Task<ContentDisposalOutcome> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return ContentDisposalOutcome.NotFound;
        }

        // Redact at the provider first so the text is no longer retrievable there; only then clear it
        // locally. If provider redaction fails, the exception surfaces and nothing is falsely reported as
        // disposed. The message resource (and thus its outcome) survives redaction.
        if (notification.ProviderMessageSid is not null)
        {
            await _sms.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
        }

        notification.DisposeContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        return ContentDisposalOutcome.Disposed;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // The provider's own record for this application's sending number over the range.
        var providerMessages = (await _sms.ListSentMessagesAsync(from, to, cancellationToken))
            .Where(m => m.SentAt is null || (m.SentAt >= from && m.SentAt <= to))
            .ToList();

        // eShop's own records: everything it handed to the provider, with delivery state refreshed so
        // each carries the provider's date_sent to compare on.
        var localWithSid = await _notificationRepository.ListAsync(
            new OrderNotificationsWithProviderSidSpecification(), cancellationToken);
        await RefreshStatusesAsync(localWithSid, cancellationToken);

        var localSent = localWithSid
            .Where(n => n.ProviderSentAt.HasValue && n.ProviderSentAt >= from && n.ProviderSentAt <= to)
            .ToList();

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First());
        var localBySid = localSent
            .Where(n => n.ProviderMessageSid is not null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var eshopOnly = new List<ReconciliationEntry>();

        foreach (var (sid, message) in providerBySid)
        {
            if (localBySid.TryGetValue(sid, out var local))
            {
                matched.Add(new ReconciliationEntry
                {
                    Sid = sid,
                    NotificationId = local.Id,
                    OrderId = local.OrderId,
                    ProviderStatus = message.Status,
                    EShopStatus = local.ProviderStatus,
                    SentAt = message.SentAt ?? local.ProviderSentAt
                });
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry
                {
                    Sid = sid,
                    ProviderStatus = message.Status,
                    SentAt = message.SentAt
                });
            }
        }

        foreach (var (sid, local) in localBySid)
        {
            if (!providerBySid.ContainsKey(sid))
            {
                eshopOnly.Add(new ReconciliationEntry
                {
                    Sid = sid,
                    NotificationId = local.Id,
                    OrderId = local.OrderId,
                    EShopStatus = local.ProviderStatus,
                    SentAt = local.ProviderSentAt
                });
            }
        }

        return new ReconciliationReport
        {
            From = from,
            To = to,
            SendingNumber = _sms.SendingNumber,
            ProviderMessageCount = providerBySid.Count,
            EShopMessageCount = localBySid.Count,
            MatchedCount = matched.Count,
            Matched = matched,
            ProviderOnly = providerOnly,
            EShopOnly = eshopOnly
        };
    }

    private async Task<IReadOnlyList<ContactNumber>> GetContactsAsync(string ownerId, CancellationToken cancellationToken)
    {
        return await _contactNumberRepository.ListAsync(
            new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
    }

    private async Task<OrderNotification> SendImmediateAsync(Order order, NotificationKind kind, string body,
        ContactNumber contact, CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(order.Id, order.BuyerId, kind, contact.Id, body);
        await _notificationRepository.AddAsync(notification, cancellationToken);

        await TrySendAsync(notification, contact.PhoneNumber, cancellationToken);
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        return notification;
    }

    private async Task ScheduleFollowUpAsync(Order order, string body, ContactNumber contact,
        DateTimeOffset sendAt, CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(order.Id, order.BuyerId, NotificationKind.DeliveryFollowUp,
            contact.Id, body, isScheduled: true, scheduledFor: sendAt);
        await _notificationRepository.AddAsync(notification, cancellationToken);

        try
        {
            var result = await _sms.ScheduleAsync(contact.PhoneNumber, body, sendAt, cancellationToken);
            ApplyResult(notification, result);
        }
        catch (Exception ex)
        {
            notification.RecordSendFailure(ex.Message);
            _logger.LogWarning("Could not schedule delivery follow-up for order {OrderId} (notification {Id}): {Error}",
                order.Id, notification.Id, ex.Message);
        }

        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var pending = await _notificationRepository.ListAsync(
            new PendingScheduledNotificationsForOrderSpecification(orderId), cancellationToken);

        foreach (var followUp in pending)
        {
            try
            {
                await _sms.CancelScheduledAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.MarkCanceled();
                await _notificationRepository.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not cancel scheduled follow-up {Id} for order {OrderId}: {Error}",
                    followUp.Id, orderId, ex.Message);
            }
        }
    }

    private async Task TrySendAsync(OrderNotification notification, string destination, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _sms.SendAsync(destination, notification.Body!, cancellationToken);
            ApplyResult(notification, result);
        }
        catch (Exception ex)
        {
            notification.RecordSendFailure(ex.Message);
            _logger.LogWarning("Could not send {Kind} notification {Id} for order {OrderId}: {Error}",
                notification.Kind, notification.Id, notification.OrderId, ex.Message);
        }
    }

    private static void ApplyResult(OrderNotification notification, SmsSendResult result)
    {
        if (result.Accepted && !string.IsNullOrEmpty(result.Sid))
        {
            notification.RecordAccepted(result.Sid!, result.Status ?? string.Empty,
                result.ErrorCode, result.ErrorMessage, result.SentAt);
        }
        else
        {
            notification.RecordSendFailure(result.ErrorMessage);
        }
    }
}
