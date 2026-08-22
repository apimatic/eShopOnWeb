using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered", "undelivered", "failed", "canceled"
    };

    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<NotificationResend> _resendRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly ITwilioMessagingClient _messagingClient;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notificationRepository,
        IRepository<NotificationResend> resendRepository,
        IRepository<ContactNumber> contactNumberRepository,
        ITwilioMessagingClient messagingClient,
        IAppLogger<OrderNotificationService> logger)
    {
        _notificationRepository = notificationRepository;
        _resendRepository = resendRepository;
        _contactNumberRepository = contactNumberRepository;
        _messagingClient = messagingClient;
        _logger = logger;
    }

    public Task<OrderNotification?> NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        return TryNotifyAsync(
            order,
            NotificationKind.OrderPlaced,
            $"Your eShopOnWeb order #{order.Id} has been placed.",
            sendAt: null,
            cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var notifications = new List<OrderNotification>();

        var dispatched = await TryNotifyAsync(
            order,
            NotificationKind.OrderDispatched,
            $"Your eShopOnWeb order #{order.Id} is on its way.",
            sendAt: null,
            cancellationToken);
        if (dispatched is not null)
        {
            notifications.Add(dispatched);
        }

        var followUp = await TryNotifyAsync(
            order,
            NotificationKind.DeliveryFollowUp,
            $"How did the delivery of your eShopOnWeb order #{order.Id} go?",
            sendAt: DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay),
            cancellationToken);
        if (followUp is not null)
        {
            notifications.Add(followUp);
        }

        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>> NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        await CancelOutstandingFollowUpsAsync(order.Id, cancellationToken);

        var notifications = new List<OrderNotification>();
        var cancelled = await TryNotifyAsync(
            order,
            NotificationKind.OrderCancelled,
            $"Your eShopOnWeb order #{order.Id} has been cancelled.",
            sendAt: null,
            cancellationToken);
        if (cancelled is not null)
        {
            notifications.Add(cancelled);
        }

        var followUps = await _notificationRepository.ListAsync(
            new ScheduledFollowUpsByOrderSpecification(order.Id), cancellationToken);
        notifications.AddRange(followUps);

        return notifications
            .GroupBy(n => n.Id)
            .Select(g => g.First())
            .OrderBy(n => n.Id)
            .ToList();
    }

    public async Task<IReadOnlyList<OrderNotification>> GetForOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return await GetForOrdersAsync(new[] { orderId }, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> GetForOrdersAsync(IEnumerable<int> orderIds, CancellationToken cancellationToken = default)
    {
        var ids = orderIds.ToArray();
        if (ids.Length == 0)
        {
            return Array.Empty<OrderNotification>();
        }

        var notifications = await _notificationRepository.ListAsync(
            new NotificationsByOrderIdsSpecification(ids), cancellationToken);

        await RefreshFromProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            throw new KeyNotFoundException("Notification was not found.");
        }

        var existingResend = await _resendRepository.FirstOrDefaultAsync(
            new NotificationResendByKeySpecification(notificationId, idempotencyKey.Trim()),
            cancellationToken);
        if (existingResend is not null)
        {
            var previous = await _notificationRepository.GetByIdAsync(existingResend.ResultNotificationId, cancellationToken);
            if (previous is not null)
            {
                await RefreshFromProviderAsync(new[] { previous }, cancellationToken);
                return previous;
            }
        }

        await RefreshFromProviderAsync(new[] { original }, cancellationToken);
        if (IsDelivered(original.DeliveryStatus))
        {
            throw new InvalidOperationException("A message that already reached the shopper cannot be resent.");
        }

        var destination = await GetActiveDestinationAsync(original, cancellationToken);
        var body = original.GetBodyForDisplay();
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new InvalidOperationException("The original message content is not available to resend.");
        }

        var resend = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            NotificationKind.Resend,
            body,
            destination?.Id);
        resend.MarkAsResendOf(original.Id);
        resend = await _notificationRepository.AddAsync(resend, cancellationToken);

        await DeliverAsync(resend, destination?.PhoneNumber, sendAt: null, cancellationToken);

        var record = new NotificationResend(original.Id, idempotencyKey.Trim(), resend.Id);
        await _resendRepository.AddAsync(record, cancellationToken);

        return resend;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            throw new KeyNotFoundException("Notification was not found.");
        }

        if (!string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            var updated = await _messagingClient.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
            notification.SyncFromProvider(
                updated.Status,
                updated.ErrorCode,
                updated.ErrorMessage,
                updated.Body,
                updated.ScheduledFor);
        }

        notification.MarkContentRedacted();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The 'to' timestamp must be on or after 'from'.");
        }

        var fromNumber = _messagingClient.FromNumber;
        var providerMessages = await _messagingClient.ListSentFromAsync(fromNumber, from, to, cancellationToken);
        var applicationNotifications = await _notificationRepository.ListAsync(
            new NotificationsInCreatedRangeSpecification(from, to), cancellationToken);

        return BuildReport(from, to, providerMessages, applicationNotifications, fromNumber);
    }

    private NotificationReconciliationReport BuildReport(
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlyList<ProviderMessage> providerMessages,
        IReadOnlyList<OrderNotification> applicationNotifications,
        string? fromNumber)
    {
        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrWhiteSpace(m.Sid))
            .GroupBy(m => m.Sid, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var applicationBySid = applicationNotifications
            .Where(n => !string.IsNullOrWhiteSpace(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var matched = new List<ReconciledMessage>();
        var providerOnly = new List<ReconciledMessage>();
        var applicationOnly = new List<ReconciledMessage>();

        foreach (var pair in providerBySid)
        {
            if (applicationBySid.TryGetValue(pair.Key, out var local))
            {
                matched.Add(new ReconciledMessage
                {
                    NotificationId = local.Id,
                    ProviderMessageSid = pair.Key,
                    DeliveryStatus = pair.Value.Status,
                    ApplicationStatus = local.DeliveryStatus
                });
            }
            else
            {
                providerOnly.Add(new ReconciledMessage
                {
                    ProviderMessageSid = pair.Key,
                    DeliveryStatus = pair.Value.Status
                });
            }
        }

        foreach (var local in applicationNotifications)
        {
            if (string.IsNullOrWhiteSpace(local.ProviderMessageSid))
            {
                applicationOnly.Add(new ReconciledMessage
                {
                    NotificationId = local.Id,
                    ApplicationStatus = local.DeliveryStatus
                });
                continue;
            }

            if (!providerBySid.ContainsKey(local.ProviderMessageSid))
            {
                applicationOnly.Add(new ReconciledMessage
                {
                    NotificationId = local.Id,
                    ProviderMessageSid = local.ProviderMessageSid,
                    ApplicationStatus = local.DeliveryStatus
                });
            }
        }

        return new NotificationReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = fromNumber ?? string.Empty,
            Matched = matched,
            ProviderOnly = providerOnly,
            ApplicationOnly = applicationOnly
        };
    }

    private async Task<OrderNotification?> TryNotifyAsync(
        Order order,
        NotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        var contact = await GetLatestActiveContactAsync(order.BuyerId, cancellationToken);
        if (contact is null)
        {
            return null;
        }

        var notification = new OrderNotification(order.Id, order.BuyerId, kind, body, contact.Id, sendAt);
        notification = await _notificationRepository.AddAsync(notification, cancellationToken);
        await DeliverAsync(notification, contact.PhoneNumber, sendAt, cancellationToken);
        return notification;
    }

    private async Task DeliverAsync(
        OrderNotification notification,
        string? to,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(to) || string.IsNullOrWhiteSpace(notification.Body))
        {
            notification.RecordProviderFailure(null, "No active contact number on file.");
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
            return;
        }

        try
        {
            var message = sendAt.HasValue
                ? await _messagingClient.ScheduleAsync(to, notification.Body, sendAt.Value, cancellationToken)
                : await _messagingClient.SendAsync(to, notification.Body, cancellationToken);

            notification.RecordProviderAcceptance(message.Sid, message.Status, sendAt ?? message.ScheduledFor);
        }
        catch (Exception)
        {
            _logger.LogWarning(
                "Failed to send notification {NotificationId} for order {OrderId}.",
                notification.Id,
                notification.OrderId);
            notification.RecordProviderFailure(null, "The provider did not accept the message.");
        }

        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    private async Task CancelOutstandingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notificationRepository.ListAsync(
            new ScheduledFollowUpsByOrderSpecification(orderId), cancellationToken);

        foreach (var followUp in followUps)
        {
            if (string.IsNullOrWhiteSpace(followUp.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var current = await _messagingClient.FetchAsync(followUp.ProviderMessageSid, cancellationToken);
                followUp.SyncFromProvider(current.Status, current.ErrorCode, current.ErrorMessage, current.Body, current.ScheduledFor);

                if (string.Equals(current.Status, "scheduled", StringComparison.OrdinalIgnoreCase))
                {
                    var cancelled = await _messagingClient.CancelScheduledAsync(followUp.ProviderMessageSid, cancellationToken);
                    followUp.SyncFromProvider(cancelled.Status, cancelled.ErrorCode, cancelled.ErrorMessage, cancelled.Body, cancelled.ScheduledFor);
                }
            }
            catch (Exception)
            {
                _logger.LogWarning(
                    "Failed to cancel scheduled follow-up notification {NotificationId} for order {OrderId}.",
                    followUp.Id,
                    followUp.OrderId);
            }

            await _notificationRepository.UpdateAsync(followUp, cancellationToken);
        }
    }

    private async Task RefreshFromProviderAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
            {
                continue;
            }

            if (notification.LastSyncedAt.HasValue
                && TerminalStatuses.Contains(notification.DeliveryStatus)
                && (DateTimeOffset.UtcNow - notification.LastSyncedAt.Value) < TimeSpan.FromSeconds(5))
            {
                continue;
            }

            try
            {
                var current = await _messagingClient.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                notification.SyncFromProvider(current.Status, current.ErrorCode, current.ErrorMessage, current.Body, current.ScheduledFor);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception)
            {
                _logger.LogWarning(
                    "Failed to refresh provider status for notification {NotificationId}.",
                    notification.Id);
            }
        }
    }

    private async Task<ContactNumber?> GetLatestActiveContactAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumberRepository.ListAsync(
            new ContactNumbersByBuyerSpecification(buyerId, activeOnly: true), cancellationToken);
        return numbers.FirstOrDefault();
    }

    private async Task<ContactNumber?> GetActiveDestinationAsync(OrderNotification original, CancellationToken cancellationToken)
    {
        if (original.ContactNumberId.HasValue)
        {
            var originalContact = await _contactNumberRepository.GetByIdAsync(original.ContactNumberId.Value, cancellationToken);
            if (originalContact is not null && originalContact.IsActive && originalContact.BuyerId == original.BuyerId)
            {
                return originalContact;
            }
        }

        return await GetLatestActiveContactAsync(original.BuyerId, cancellationToken);
    }

    private static bool IsDelivered(string status) =>
        string.Equals(status, "delivered", StringComparison.OrdinalIgnoreCase);
}
