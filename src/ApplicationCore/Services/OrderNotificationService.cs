using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<NotificationResendRecord> _resendRecords;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ITwilioMessagingClient _messaging;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notifications,
        IRepository<NotificationResendRecord> resendRecords,
        IRepository<ContactNumber> contactNumbers,
        ITwilioMessagingClient messaging,
        IAppLogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _resendRecords = resendRecords;
        _contactNumbers = contactNumbers;
        _messaging = messaging;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        return TrySendAsync(order, NotificationKind.OrderPlaced, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await TrySendAsync(order, NotificationKind.OrderDispatched, cancellationToken);
        await TrySendAsync(order, NotificationKind.DeliveryFollowUp, cancellationToken, DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay));
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        await CancelScheduledFollowUpsAsync(order.Id, cancellationToken);
        await TrySendAsync(order, NotificationKind.OrderCancelled, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, string? buyerId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderIdSpecification(orderId), cancellationToken);
        if (buyerId != null && notifications.Any(n => n.BuyerId != buyerId))
        {
            return Array.Empty<OrderNotification>();
        }

        if (buyerId != null && notifications.Count == 0)
        {
            return notifications;
        }

        await RefreshFromProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrdersAsync(IEnumerable<int> orderIds, CancellationToken cancellationToken = default)
    {
        var ids = orderIds.ToArray();
        if (ids.Length == 0)
        {
            return Array.Empty<OrderNotification>();
        }

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderIdsSpecification(ids), cancellationToken);
        await RefreshFromProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.");
        }

        var existingKey = await _resendRecords.FirstOrDefaultAsync(
            new NotificationResendByIdempotencyKeySpecification(idempotencyKey.Trim()), cancellationToken);
        if (existingKey != null)
        {
            var previous = await _notifications.GetByIdAsync(existingKey.ResultingNotificationId, cancellationToken);
            if (previous != null)
            {
                return previous;
            }
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original == null)
        {
            throw new EntityNotFoundException("Notification was not found.");
        }

        var destination = await GetActiveDestinationAsync(original.BuyerId, cancellationToken);
        if (destination == null)
        {
            throw new InvalidOperationException("The shopper has no contact number on file.");
        }

        var body = original.ContentRedacted || string.IsNullOrEmpty(original.Body)
            ? BodyFor(original.Kind == NotificationKind.Resend ? NotificationKind.OrderPlaced : original.Kind, original.OrderId)
            : original.Body;

        var sent = await TrySendCoreAsync(
            original.OrderId,
            original.BuyerId,
            NotificationKind.Resend,
            body,
            destination,
            sendAt: null,
            originalNotificationId: original.Id,
            cancellationToken);

        var record = new NotificationResendRecord(idempotencyKey.Trim(), original.Id, sent.Id);
        await _resendRecords.AddAsync(record, cancellationToken);
        return sent;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification == null)
        {
            throw new EntityNotFoundException("Notification was not found.");
        }

        if (!string.IsNullOrEmpty(notification.ProviderSid))
        {
            try
            {
                var snapshot = await _messaging.RedactBodyAsync(notification.ProviderSid, cancellationToken);
                notification.ApplyProviderState(
                    snapshot.Status,
                    snapshot.ErrorCode,
                    snapshot.ErrorMessage,
                    snapshot.Body,
                    snapshot.DateSent);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to redact provider content for notification {NotificationId} ({ProviderSid}): {Error}",
                    notification.Id, notification.ProviderSid, ex.GetType().Name);
                throw;
            }
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var fromNumber = _messaging.ConfiguredFromNumber;
        var providerMessages = await _messaging.ListSentFromAsync(fromNumber, from, to, cancellationToken);
        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var localBySid = new Dictionary<string, OrderNotification>(StringComparer.Ordinal);
        if (providerBySid.Count > 0)
        {
            var bySid = await _notifications.ListAsync(
                new OrderNotificationsByProviderSidsSpecification(providerBySid.Keys), cancellationToken);
            foreach (var local in bySid)
            {
                if (!string.IsNullOrEmpty(local.ProviderSid))
                {
                    localBySid[local.ProviderSid] = local;
                }
            }
        }

        var localInRange = await _notifications.ListAsync(
            new OrderNotificationsCreatedInRangeSpecification(from, to), cancellationToken);

        var matched = new List<ReconciledMessage>();
        var providerOnly = new List<SmsMessageSnapshot>();
        var matchedSids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var provider in providerMessages)
        {
            if (!string.IsNullOrEmpty(provider.Sid) && localBySid.TryGetValue(provider.Sid, out var local))
            {
                matched.Add(new ReconciledMessage(local, provider));
                matchedSids.Add(provider.Sid);
            }
            else
            {
                providerOnly.Add(provider);
            }
        }

        var localOnly = localInRange
            .Where(n => string.IsNullOrEmpty(n.ProviderSid) || !matchedSids.Contains(n.ProviderSid))
            .ToList();

        return new NotificationReconciliationReport(from, to, fromNumber, matched, providerOnly, localOnly);
    }

    private async Task TrySendAsync(Order order, NotificationKind kind, CancellationToken cancellationToken, DateTimeOffset? sendAt = null)
    {
        var destination = await GetActiveDestinationAsync(order.BuyerId, cancellationToken);
        if (destination == null)
        {
            return;
        }

        var body = BodyFor(kind, order.Id);
        await TrySendCoreAsync(order.Id, order.BuyerId, kind, body, destination, sendAt, originalNotificationId: null, cancellationToken);
    }

    private async Task<OrderNotification> TrySendCoreAsync(
        int orderId,
        string buyerId,
        NotificationKind kind,
        string body,
        string destination,
        DateTimeOffset? sendAt,
        int? originalNotificationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _messaging.SendAsync(new SmsSendRequest
            {
                To = destination,
                Body = body,
                SendAt = sendAt
            }, cancellationToken);

            var sent = new OrderNotification(
                orderId,
                buyerId,
                kind,
                body,
                snapshot.Sid,
                string.IsNullOrEmpty(snapshot.Status) ? "queued" : snapshot.Status,
                snapshot.ErrorCode,
                snapshot.ErrorMessage,
                sendAt,
                snapshot.DateSent,
                originalNotificationId);
            return await _notifications.AddAsync(sent, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to send {Kind} notification for order {OrderId}: {Error}",
                kind, orderId, ex.GetType().Name);

            var failed = new OrderNotification(
                orderId,
                buyerId,
                kind,
                body,
                providerSid: null,
                status: "failed",
                errorMessage: "The provider did not accept the message.",
                sendAt: sendAt,
                originalNotificationId: originalNotificationId);
            return await _notifications.AddAsync(failed, cancellationToken);
        }
    }

    private async Task CancelScheduledFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notifications.ListAsync(new ScheduledFollowUpByOrderIdSpecification(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            if (string.IsNullOrEmpty(followUp.ProviderSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _messaging.CancelAsync(followUp.ProviderSid, cancellationToken);
                followUp.ApplyProviderState(
                    snapshot.Status,
                    snapshot.ErrorCode,
                    snapshot.ErrorMessage,
                    snapshot.Body,
                    snapshot.DateSent);
                await _notifications.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to cancel scheduled follow-up {NotificationId} ({ProviderSid}): {Error}",
                    followUp.Id, followUp.ProviderSid, ex.GetType().Name);

                try
                {
                    var current = await _messaging.FetchAsync(followUp.ProviderSid, cancellationToken);
                    followUp.ApplyProviderState(
                        current.Status,
                        current.ErrorCode,
                        current.ErrorMessage,
                        current.Body,
                        current.DateSent);
                    await _notifications.UpdateAsync(followUp, cancellationToken);
                }
                catch (Exception fetchEx)
                {
                    _logger.LogWarning("Failed to refresh scheduled follow-up {NotificationId} after cancel error: {Error}",
                        followUp.Id, fetchEx.GetType().Name);
                }
            }
        }
    }

    private async Task RefreshFromProviderAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications.Where(n => !string.IsNullOrEmpty(n.ProviderSid)))
        {
            try
            {
                var snapshot = await _messaging.FetchAsync(notification.ProviderSid!, cancellationToken);
                notification.ApplyProviderState(
                    snapshot.Status,
                    snapshot.ErrorCode,
                    snapshot.ErrorMessage,
                    snapshot.Body,
                    snapshot.DateSent);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to refresh notification {NotificationId} ({ProviderSid}): {Error}",
                    notification.Id, notification.ProviderSid ?? string.Empty, ex.GetType().Name);
            }
        }
    }

    private async Task<string?> GetActiveDestinationAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerIdSpecification(buyerId), cancellationToken);
        return numbers.FirstOrDefault()?.PhoneNumber;
    }

    private static string BodyFor(NotificationKind kind, int orderId)
    {
        return kind switch
        {
            NotificationKind.OrderPlaced => $"Your eShopOnWeb order #{orderId} has been placed.",
            NotificationKind.OrderDispatched => $"Your eShopOnWeb order #{orderId} is on its way.",
            NotificationKind.DeliveryFollowUp => $"How did the delivery of your eShopOnWeb order #{orderId} go?",
            NotificationKind.OrderCancelled => $"Your eShopOnWeb order #{orderId} has been cancelled.",
            _ => $"An update is available for your eShopOnWeb order #{orderId}."
        };
    }
}
