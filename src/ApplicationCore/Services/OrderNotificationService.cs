using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private static readonly HashSet<string> TerminalFailureStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "failed",
        "undelivered",
        "canceled",
        "send_failed"
    };

    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IContactNumberService _contactNumberService;
    private readonly ITwilioMessagingClient _messagingClient;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notificationRepository,
        IContactNumberService contactNumberService,
        ITwilioMessagingClient messagingClient,
        IAppLogger<OrderNotificationService> logger)
    {
        _notificationRepository = notificationRepository;
        _contactNumberService = contactNumberService;
        _messagingClient = messagingClient;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(order, nameof(order));
        var body = $"eShopOnWeb: your order #{order.Id} has been placed. Total {order.Total():C}.";
        return SendBestEffortAsync(order, NotificationKind.OrderPlaced, body, sendAt: null, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(order, nameof(order));
        var body = $"eShopOnWeb: your order #{order.Id} is on its way.";
        await SendBestEffortAsync(order, NotificationKind.OrderDispatched, body, sendAt: null, cancellationToken);

        var followUpBody = $"eShopOnWeb: how did the delivery of order #{order.Id} go?";
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        await SendBestEffortAsync(order, NotificationKind.DeliveryFollowUp, followUpBody, sendAt, cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(order, nameof(order));
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        var body = $"eShopOnWeb: your order #{order.Id} has been cancelled.";
        await SendBestEffortAsync(order, NotificationKind.OrderCancelled, body, sendAt: null, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(
        int orderId,
        CancellationToken cancellationToken = default)
    {
        var spec = new NotificationsByOrderIdSpecification(orderId);
        var notifications = await _notificationRepository.ListAsync(spec, cancellationToken);

        foreach (var notification in notifications)
        {
            await SyncFromProviderAsync(notification, cancellationToken);
        }

        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(
        int notificationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var existingResendSpec = new ResendByIdempotencyKeySpecification(notificationId, idempotencyKey);
        var existingResend = await _notificationRepository.FirstOrDefaultAsync(existingResendSpec, cancellationToken);
        if (existingResend is not null)
        {
            await SyncFromProviderAsync(existingResend, cancellationToken);
            return existingResend;
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            throw new NotificationNotFoundException(notificationId);
        }

        await SyncFromProviderAsync(original, cancellationToken);

        if (!DidNotReachShopper(original.ProviderStatus))
        {
            throw new ConflictException(
                $"Notification {notificationId} cannot be resent because its current status is '{original.ProviderStatus}'.");
        }

        var stillRegistered = await _contactNumberService.IsNumberActiveForBuyerAsync(
            original.BuyerId, original.DestinationNumber, cancellationToken);
        if (!stillRegistered)
        {
            throw new ConflictException(
                "The destination for this notification is no longer on file and cannot be messaged again.");
        }

        var body = original.Body;
        if (string.IsNullOrEmpty(body))
        {
            throw new ConflictException("The original message body is no longer available to resend.");
        }

        var sent = await TryCreateMessageAsync(original.DestinationNumber, body, sendAt: null, cancellationToken);

        var resend = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            NotificationKind.Resend,
            original.DestinationNumber,
            body,
            sent?.Sid,
            sent?.Status ?? "send_failed",
            sourceNotificationId: original.Id,
            idempotencyKey: idempotencyKey,
            errorCode: sent?.ErrorCode,
            sendFailureReason: sent is null ? "Provider rejected or failed the resend." : null);

        return await _notificationRepository.AddAsync(resend, cancellationToken);
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            throw new NotificationNotFoundException(notificationId);
        }

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            try
            {
                var updated = await _messagingClient.RedactMessageBodyAsync(notification.ProviderMessageSid, cancellationToken);
                notification.ApplyProviderState(updated.Status, updated.ErrorCode, updated.Sid);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to redact provider content for notification {NotificationId}: {Message}", notificationId, ex.Message);
                throw;
            }
        }

        notification.RedactLocalContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var fromNumber = _messagingClient.FromNumber;
        var providerMessages = await _messagingClient.ListMessagesFromNumberAsync(fromNumber, from, to, cancellationToken);

        var localSpec = new NotificationsWithProviderSidInRangeSpecification(from.AddDays(-1), to.AddDays(1));
        var localNotifications = await _notificationRepository.ListAsync(localSpec, cancellationToken);
        var localBySid = localNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var items = new List<NotificationReconciliationItem>();
        var matchedSids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var provider in providerMessages)
        {
            if (localBySid.TryGetValue(provider.Sid, out var local))
            {
                matchedSids.Add(provider.Sid);
                items.Add(new NotificationReconciliationItem(
                    provider.Sid,
                    local.Id,
                    provider.Status,
                    local.ProviderStatus,
                    "matched"));
            }
            else
            {
                items.Add(new NotificationReconciliationItem(
                    provider.Sid,
                    null,
                    provider.Status,
                    null,
                    "provider_only"));
            }
        }

        foreach (var local in localNotifications)
        {
            if (string.IsNullOrEmpty(local.ProviderMessageSid) || matchedSids.Contains(local.ProviderMessageSid))
            {
                continue;
            }

            items.Add(new NotificationReconciliationItem(
                local.ProviderMessageSid,
                local.Id,
                null,
                local.ProviderStatus,
                "local_only"));
        }

        return new NotificationReconciliationReport(
            from,
            to,
            fromNumber,
            items,
            items.Count(i => i.Match == "provider_only"),
            items.Count(i => i.Match == "local_only"),
            items.Count(i => i.Match == "matched"));
    }

    private async Task SendBestEffortAsync(
        Order order,
        NotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var contact = await _contactNumberService.GetActiveForBuyerAsync(order.BuyerId, cancellationToken);
            if (contact is null)
            {
                _logger.LogInformation("No contact number on file for order {OrderId}; skipping {Kind} notification.", order.Id, kind);
                return;
            }

            var sent = await TryCreateMessageAsync(contact.PhoneNumber, body, sendAt, cancellationToken);
            var notification = new OrderNotification(
                order.Id,
                order.BuyerId,
                kind,
                contact.PhoneNumber,
                body,
                sent?.Sid,
                sent?.Status ?? "send_failed",
                scheduledSendAt: sendAt,
                errorCode: sent?.ErrorCode,
                sendFailureReason: sent is null ? "Provider did not accept the message." : null);

            await _notificationRepository.AddAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to send {Kind} notification for order {OrderId}: {Message}", kind, order.Id, ex.Message);
        }
    }

    private async Task<TwilioMessageResult?> TryCreateMessageAsync(
        string to,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _messagingClient.CreateMessageAsync(to, body, sendAt, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Provider create-message failed: {Message}", ex.Message);
            return null;
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var spec = new ScheduledFollowUpByOrderSpecification(orderId);
        var pending = await _notificationRepository.ListAsync(spec, cancellationToken);

        foreach (var followUp in pending)
        {
            if (string.IsNullOrEmpty(followUp.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var updated = await _messagingClient.CancelMessageAsync(followUp.ProviderMessageSid, cancellationToken);
                followUp.ApplyProviderState(updated.Status, updated.ErrorCode, updated.Sid);
                await _notificationRepository.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Failed to cancel scheduled follow-up notification {NotificationId}: {Message}",
                    followUp.Id,
                    ex.Message);
            }
        }
    }

    private async Task SyncFromProviderAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            return;
        }

        try
        {
            var current = await _messagingClient.FetchMessageAsync(notification.ProviderMessageSid, cancellationToken);
            notification.ApplyProviderState(current.Status, current.ErrorCode, current.Sid);
            if (current.Body == string.Empty)
            {
                notification.RedactLocalContent();
            }
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Failed to refresh provider status for notification {NotificationId}: {Message}",
                notification.Id,
                ex.Message);
        }
    }

    private static bool DidNotReachShopper(string status) =>
        TerminalFailureStatuses.Contains(status);
}
