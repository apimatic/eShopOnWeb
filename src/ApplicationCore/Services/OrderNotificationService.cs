using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Extensions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ResendLocks = new();

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
        var body = $"eShopOnWeb: your order #{order.Id} has been placed. Total {order.Total():C}.";
        return SendImmediateAsync(order, OrderNotificationKind.OrderPlaced, body, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var dispatchedBody = $"eShopOnWeb: your order #{order.Id} is on its way.";
        await SendImmediateAsync(order, OrderNotificationKind.OrderDispatched, dispatchedBody, cancellationToken);

        var followUpBody = $"eShopOnWeb: how did the delivery of order #{order.Id} go? Reply with your feedback.";
        await ScheduleFollowUpAsync(order, followUpBody, cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        var body = $"eShopOnWeb: your order #{order.Id} has been cancelled.";
        await SendImmediateAsync(order, OrderNotificationKind.OrderCancelled, body, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.ListAsync(
            new OrderNotificationsByOrderSpecification(orderId),
            cancellationToken);
        await RefreshStatusesAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.ListAsync(
            new OrderNotificationsByBuyerSpecification(buyerId),
            cancellationToken);
        await RefreshStatusesAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        var gate = ResendLocks.GetOrAdd($"{notificationId}:{idempotencyKey}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var existingResend = await _notificationRepository.FirstOrDefaultAsync(
                new OrderNotificationByResendIdempotencySpecification(notificationId, idempotencyKey),
                cancellationToken);
            if (existingResend != null)
            {
                return existingResend;
            }

            var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken)
                ?? throw new KeyNotFoundException("Notification was not found.");

            await RefreshStatusesAsync(new[] { original }, cancellationToken);

            if (original.ContentRedacted || string.IsNullOrEmpty(original.Body))
            {
                throw new InvalidOperationException("The message content has been disposed and cannot be re-sent.");
            }

            if (!original.DidNotReachShopper())
            {
                throw new InvalidOperationException($"The message cannot be re-sent because its current outcome is '{original.ProviderStatus}'.");
            }

            var destination = await _contactNumberService.GetPreferredForBuyerAsync(original.BuyerId, cancellationToken);
            if (destination == null)
            {
                throw new InvalidOperationException("The shopper no longer has a registered contact number.");
            }

            var resend = new OrderNotification(
                original.OrderId,
                original.BuyerId,
                OrderNotificationKind.Resend,
                original.Body,
                destination.CanonicalNumber,
                destination.Id);
            resend.AssignResend(original.Id, idempotencyKey);
            resend = await _notificationRepository.AddAsync(resend, cancellationToken);

            await DispatchToProviderAsync(resend, schedule: false, sendAt: null, cancellationToken);
            return resend;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<OrderNotification> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException("Notification was not found.");

        if (!string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            var updated = await _messagingClient.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
            notification.ApplyProviderResult(updated.Sid, updated.Status, updated.ErrorCode, updated.ErrorMessage, updated.Body);
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        return notification;
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The 'to' timestamp must be on or after 'from'.");
        }

        var fromNumber = _messagingClient.FromNumber;

        // Match the provider query by asking Twilio for this From number's messages over the whole range.
        var providerMessages = await _messagingClient.ListFromNumberAsync(fromNumber, from, to, cancellationToken);
        var applicationNotifications = await _notificationRepository.ListAsync(
            new OrderNotificationsWithProviderSidInRangeSpecification(from, to),
            cancellationToken);

        // Include any of our SIDs that appear in the provider window even if CreatedAt is outside it.
        var providerSids = providerMessages
            .Select(m => m.Sid)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var extra = await LoadByProviderSidsAsync(providerSids.Except(
            applicationNotifications.Select(n => n.ProviderMessageSid!),
            StringComparer.OrdinalIgnoreCase).ToArray(), cancellationToken);
        applicationNotifications = applicationNotifications.Concat(extra).ToList();

        var applicationBySid = applicationNotifications
            .Where(n => !string.IsNullOrWhiteSpace(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var matched = new List<ReconciledNotification>();
        var providerOnly = new List<ProviderOnlyMessage>();
        foreach (var message in providerMessages)
        {
            if (applicationBySid.TryGetValue(message.Sid, out var notification))
            {
                matched.Add(new ReconciledNotification(
                    notification.Id,
                    message.Sid,
                    notification.ProviderStatus,
                    message.Status));
            }
            else
            {
                providerOnly.Add(new ProviderOnlyMessage(message.Sid, message.Status, message.DateSent));
            }
        }

        var applicationOnly = applicationNotifications
            .Where(n => n.ProviderMessageSid != null && !providerSids.Contains(n.ProviderMessageSid))
            .Select(n => new ApplicationOnlyNotification(n.Id, n.ProviderMessageSid, n.ProviderStatus))
            .ToList();

        return new NotificationReconciliationReport(from, to, fromNumber, matched, providerOnly, applicationOnly);
    }

    private async Task SendImmediateAsync(Order order, OrderNotificationKind kind, string body, CancellationToken cancellationToken)
    {
        var contact = await _contactNumberService.GetPreferredForBuyerAsync(order.BuyerId, cancellationToken);
        var notification = new OrderNotification(
            order.Id,
            order.BuyerId,
            kind,
            body,
            contact?.CanonicalNumber,
            contact?.Id);

        if (contact == null)
        {
            notification.MarkNotSent("No contact number on file.");
            await _notificationRepository.AddAsync(notification, cancellationToken);
            return;
        }

        notification = await _notificationRepository.AddAsync(notification, cancellationToken);
        await DispatchToProviderAsync(notification, schedule: false, sendAt: null, cancellationToken);
    }

    private async Task ScheduleFollowUpAsync(Order order, string body, CancellationToken cancellationToken)
    {
        var contact = await _contactNumberService.GetPreferredForBuyerAsync(order.BuyerId, cancellationToken);
        var sendAt = DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay);
        var notification = new OrderNotification(
            order.Id,
            order.BuyerId,
            OrderNotificationKind.DeliveryFollowUp,
            body,
            contact?.CanonicalNumber,
            contact?.Id);
        notification.MarkScheduled(sendAt);

        if (contact == null)
        {
            notification.MarkNotSent("No contact number on file.");
            await _notificationRepository.AddAsync(notification, cancellationToken);
            return;
        }

        notification = await _notificationRepository.AddAsync(notification, cancellationToken);
        await DispatchToProviderAsync(notification, schedule: true, sendAt: sendAt, cancellationToken);
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notificationRepository.ListAsync(
            new ScheduledFollowUpNotificationsByOrderSpecification(orderId),
            cancellationToken);

        foreach (var followUp in followUps)
        {
            try
            {
                var current = await _messagingClient.FetchAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.ApplyProviderResult(current.Sid, current.Status, current.ErrorCode, current.ErrorMessage, current.Body);

                if (string.Equals(current.Status, "scheduled", StringComparison.OrdinalIgnoreCase))
                {
                    var cancelled = await _messagingClient.CancelScheduledAsync(followUp.ProviderMessageSid!, cancellationToken);
                    followUp.ApplyProviderResult(cancelled.Sid, cancelled.Status, cancelled.ErrorCode, cancelled.ErrorMessage, cancelled.Body);
                    _logger.LogInformation("Cancelled scheduled follow-up notification {NotificationId} for order {OrderId}.", followUp.Id, orderId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Could not cancel scheduled follow-up notification {NotificationId} for order {OrderId}: {Message}",
                    followUp.Id,
                    orderId,
                    LogSanitizer.RedactPhoneNumbers(ex.Message));
            }

            await _notificationRepository.UpdateAsync(followUp, cancellationToken);
        }
    }

    private async Task DispatchToProviderAsync(
        OrderNotification notification,
        bool schedule,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(notification.DestinationNumber) || string.IsNullOrEmpty(notification.Body))
        {
            notification.MarkNotSent("No destination or body available.");
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
            return;
        }

        try
        {
            TwilioMessageSnapshot result = schedule && sendAt.HasValue
                ? await _messagingClient.ScheduleAsync(notification.DestinationNumber, notification.Body, sendAt.Value, cancellationToken)
                : await _messagingClient.SendAsync(notification.DestinationNumber, notification.Body, cancellationToken);

            notification.ApplyProviderResult(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage, result.Body);
            if (schedule && sendAt.HasValue)
            {
                notification.MarkScheduled(sendAt.Value);
                if (!string.IsNullOrWhiteSpace(result.Status))
                {
                    notification.ApplyProviderResult(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage);
                }
            }

            _logger.LogInformation(
                "Submitted notification {NotificationId} for order {OrderId} as provider message {MessageSid} with status {Status}.",
                notification.Id,
                notification.OrderId,
                result.Sid,
                result.Status ?? "unknown");
        }
        catch (Exception ex)
        {
            notification.ApplyProviderResult(null, "failed", null, ex.Message);
            _logger.LogWarning(
                "Provider rejected notification {NotificationId} for order {OrderId}: {Message}",
                notification.Id,
                notification.OrderId,
                LogSanitizer.RedactPhoneNumbers(ex.Message));
        }

        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    private async Task RefreshStatusesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications.Where(n => !string.IsNullOrWhiteSpace(n.ProviderMessageSid)))
        {
            try
            {
                var current = await _messagingClient.FetchAsync(notification.ProviderMessageSid!, cancellationToken);
                var body = notification.ContentRedacted ? null : current.Body;
                notification.ApplyProviderResult(current.Sid, current.Status, current.ErrorCode, current.ErrorMessage, body);
                if (notification.ContentRedacted)
                {
                    notification.RedactContent();
                }
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Could not refresh provider status for notification {NotificationId}: {Message}",
                    notification.Id,
                    LogSanitizer.RedactPhoneNumbers(ex.Message));
            }
        }
    }

    private async Task<List<OrderNotification>> LoadByProviderSidsAsync(string[] sids, CancellationToken cancellationToken)
    {
        if (sids.Length == 0)
        {
            return new List<OrderNotification>();
        }

        var all = await _notificationRepository.ListAsync(cancellationToken);
        return all.Where(n => n.ProviderMessageSid != null && sids.Contains(n.ProviderMessageSid, StringComparer.OrdinalIgnoreCase)).ToList();
    }
}
