using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IReadRepository<ContactNumber> _contactNumberRepository;
    private readonly ISmsProviderClient _smsProviderClient;
    private readonly TwilioOptions _options;
    private readonly ILogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notificationRepository,
        IReadRepository<ContactNumber> contactNumberRepository,
        ISmsProviderClient smsProviderClient,
        IOptions<TwilioOptions> options,
        ILogger<OrderNotificationService> logger)
    {
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _smsProviderClient = smsProviderClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: your order #{order.Id} has been placed. Thank you for shopping with us!";
        await SendToShopperAsync(order, body, NotificationType.OrderPlaced, null, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: good news — your order #{order.Id} is on its way!";
        await SendToShopperAsync(order, body, NotificationType.OrderDispatched, null, cancellationToken);

        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var followUpBody = $"eShopOnWeb: your order #{order.Id} should have arrived by now. How did the delivery go?";
        await SendToShopperAsync(order, followUpBody, NotificationType.DeliveryFollowUp, sendAt, cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        // A queued follow-up for a cancelled order must never reach the shopper.
        var scheduledFollowUps = await _notificationRepository.ListAsync(
            new ScheduledFollowUpsByOrderSpecification(order.Id), cancellationToken);
        foreach (var followUp in scheduledFollowUps)
        {
            try
            {
                var cancelled = await _smsProviderClient.CancelScheduledMessageAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.UpdateProviderStatus(cancelled.Status, cancelled.ErrorCode, cancelled.ErrorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cancel scheduled follow-up {MessageSid} for order {OrderId}.", followUp.ProviderMessageSid, order.Id);
                followUp.MarkFailed("cancel-failed", null, ex.Message);
            }
            await _notificationRepository.UpdateAsync(followUp, cancellationToken);
        }

        var body = $"eShopOnWeb: your order #{order.Id} has been cancelled. If this is unexpected, please contact support.";
        await SendToShopperAsync(order, body, NotificationType.OrderCancelled, null, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.ListAsync(
            new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshStatusesAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>> GetNotificationsForOrdersAsync(IEnumerable<int> orderIds, CancellationToken cancellationToken = default)
    {
        var ids = orderIds.ToList();
        if (ids.Count == 0)
        {
            return new List<OrderNotification>();
        }

        var notifications = await _notificationRepository.ListAsync(
            new OrderNotificationsByOrderSpecification(ids), cancellationToken);
        await RefreshStatusesAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (existing != null)
        {
            return new ResendResult { Notification = existing, WasDuplicate = true };
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original == null)
        {
            throw new NotificationNotFoundException(notificationId);
        }
        if (original.ContentRedacted || original.Body == null)
        {
            throw new InvalidOperationException($"Notification {notificationId} content has been disposed of and cannot be resent.");
        }

        var resend = new OrderNotification(original.OrderId, original.BuyerId, original.ToNumber, original.Body, original.Type);
        resend.MarkIdempotencyKey(idempotencyKey);
        await SendAndRecordAsync(resend, cancellationToken);

        return new ResendResult { Notification = resend, WasDuplicate = false };
    }

    public async Task<OrderNotification> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification == null)
        {
            throw new NotificationNotFoundException(notificationId);
        }

        if (!notification.ContentRedacted)
        {
            if (notification.ProviderMessageSid != null)
            {
                // Dispose of the text at the provider too, so it is no longer retrievable there.
                await _smsProviderClient.RedactMessageBodyAsync(notification.ProviderMessageSid, cancellationToken);
            }
            notification.RedactContent();
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }

        return notification;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _smsProviderClient.ListMessagesAsync(_options.FromNumber, from, to, cancellationToken);
        var localNotifications = await _notificationRepository.ListAsync(
            new OrderNotificationsCreatedBetweenSpecification(from, to), cancellationToken);

        var localBySid = localNotifications
            .Where(n => n.ProviderMessageSid != null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var report = new ReconciliationReport { From = from, To = to, FromNumber = _options.FromNumber };

        foreach (var message in providerMessages)
        {
            localBySid.TryGetValue(message.Sid, out var match);
            report.ProviderMessages.Add(new ReconciledProviderMessage
            {
                MessageSid = message.Sid,
                To = message.To,
                Status = message.Status,
                DateSent = message.DateSent,
                ErrorCode = message.ErrorCode,
                MatchedNotificationId = match?.Id,
                OrderId = match?.OrderId,
                NotificationType = match?.Type.ToString()
            });
        }

        var providerSids = new HashSet<string>(providerMessages.Select(m => m.Sid));
        foreach (var notification in localNotifications)
        {
            if (notification.ProviderMessageSid == null || !providerSids.Contains(notification.ProviderMessageSid))
            {
                report.LocalOnlyNotifications.Add(new UnmatchedLocalNotification
                {
                    NotificationId = notification.Id,
                    OrderId = notification.OrderId,
                    ProviderMessageSid = notification.ProviderMessageSid,
                    Status = notification.ProviderStatus,
                    NotificationType = notification.Type.ToString()
                });
            }
        }

        return report;
    }

    private async Task SendToShopperAsync(Order order, string body, NotificationType type, DateTimeOffset? scheduleFor, CancellationToken cancellationToken)
    {
        var contactNumbers = await _contactNumberRepository.ListAsync(
            new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
        var destination = contactNumbers.FirstOrDefault();
        if (destination == null)
        {
            // A shopper with no number on file is simply not messaged.
            return;
        }

        var notification = new OrderNotification(order.Id, order.BuyerId, destination.PhoneNumber, body, type, scheduleFor);
        await SendAndRecordAsync(notification, cancellationToken);
    }

    private async Task SendAndRecordAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            ProviderMessage sent = notification.ScheduledFor.HasValue
                ? await _smsProviderClient.ScheduleMessageAsync(notification.ToNumber, notification.Body!, notification.ScheduledFor.Value, cancellationToken)
                : await _smsProviderClient.SendMessageAsync(notification.ToNumber, notification.Body!, cancellationToken);
            notification.MarkAccepted(sent.Sid, sent.Status);
        }
        catch (Exception ex)
        {
            // A message that cannot be sent must never fail the underlying operation.
            var status = ex is SmsProviderException ? "failed" : "send-error";
            notification.MarkFailed(status, (ex as SmsProviderException)?.ProviderErrorCode, ex.Message);
            _logger.LogError(ex, "Failed to send {NotificationType} notification for order {OrderId}.", notification.Type, notification.OrderId);
        }

        await _notificationRepository.AddAsync(notification, cancellationToken);
    }

    private async Task RefreshStatusesAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        // There is no callback URL the provider can reach, so current delivery
        // outcomes are obtained by asking the provider.
        foreach (var notification in notifications.Where(n => n.ProviderMessageSid != null && !n.IsInTerminalState))
        {
            try
            {
                var current = await _smsProviderClient.FetchMessageAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.UpdateProviderStatus(current.Status, current.ErrorCode, current.ErrorMessage);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh status for message {MessageSid}.", notification.ProviderMessageSid);
            }
        }
    }
}
