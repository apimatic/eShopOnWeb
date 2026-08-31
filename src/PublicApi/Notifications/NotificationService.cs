using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

/// <summary>
/// Orchestrates order notifications: sends/schedules messages through the provider and keeps
/// the local notification records in step with provider-owned state. A provider failure never
/// escapes these methods as a fault of the underlying order operation — it is recorded on the
/// notification instead. Phone numbers are never logged.
/// </summary>
public class NotificationService
{
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered", "failed", "undelivered", "canceled"
    };

    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly TwilioMessagingService _messaging;
    private readonly TwilioOptions _options;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        IRepository<OrderNotification> notifications,
        IRepository<ContactNumber> contactNumbers,
        TwilioMessagingService messaging,
        IOptions<TwilioOptions> options,
        ILogger<NotificationService> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _messaging = messaging;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Sends an immediate message to every number the shopper has on file.</summary>
    public async Task<IReadOnlyList<OrderNotification>> NotifyAsync(
        Order order, NotificationKind kind, string body, CancellationToken ct)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(order.BuyerId), ct);
        var created = new List<OrderNotification>();

        foreach (var number in numbers)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, number.Id, number.PhoneNumber, kind, body);
            await _notifications.AddAsync(notification, ct);

            try
            {
                var sent = await _messaging.SendMessageAsync(number.PhoneNumber, body, ct);
                notification.MarkAccepted(sent.Sid, sent.Status);
            }
            catch (MessagingException ex)
            {
                _logger.LogWarning(ex, "Notification {NotificationId} for order {OrderId} could not be sent (provider status {ProviderStatus}).",
                    notification.Id, order.Id, (int?)ex.ProviderStatusCode);
                notification.MarkSendFailed(ex.Message);
            }

            await _notifications.UpdateAsync(notification, ct);
            created.Add(notification);
        }

        return created;
    }

    /// <summary>Queues the delivery follow-up with the provider for a later date.</summary>
    public async Task<IReadOnlyList<OrderNotification>> ScheduleFollowUpAsync(
        Order order, string body, CancellationToken ct)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(order.BuyerId), ct);
        var sendAt = DateTimeOffset.UtcNow.AddDays(_options.FollowUpDelayDays);
        var created = new List<OrderNotification>();

        foreach (var number in numbers)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, number.Id, number.PhoneNumber,
                NotificationKind.DeliveryFollowUp, body, scheduledFor: sendAt);
            await _notifications.AddAsync(notification, ct);

            try
            {
                var scheduled = await _messaging.ScheduleMessageAsync(number.PhoneNumber, body, sendAt, ct);
                notification.MarkAccepted(scheduled.Sid, scheduled.Status);
            }
            catch (MessagingException ex)
            {
                _logger.LogWarning(ex, "Follow-up notification {NotificationId} for order {OrderId} could not be scheduled (provider status {ProviderStatus}).",
                    notification.Id, order.Id, (int?)ex.ProviderStatusCode);
                notification.MarkSendFailed(ex.Message);
            }

            await _notifications.UpdateAsync(notification, ct);
            created.Add(notification);
        }

        return created;
    }

    /// <summary>
    /// Cancels every not-yet-sent follow-up for the order at the provider, so a cancelled
    /// order's "how was your delivery?" message never reaches the shopper.
    /// </summary>
    public async Task CancelPendingFollowUpsAsync(Order order, CancellationToken ct)
    {
        var followUps = await _notifications.ListAsync(new FollowUpNotificationsByOrderSpecification(order.Id), ct);

        foreach (var followUp in followUps)
        {
            if (TerminalStatuses.Contains(followUp.Status))
            {
                continue;
            }

            try
            {
                var outcome = await _messaging.CancelScheduledMessageAsync(followUp.ProviderMessageSid!, ct);
                followUp.UpdateProviderOutcome(outcome.Status, outcome.ErrorCode, outcome.ErrorMessage);
            }
            catch (MessagingException ex)
            {
                _logger.LogWarning(ex, "Scheduled follow-up {NotificationId} for order {OrderId} could not be cancelled (provider status {ProviderStatus}).",
                    followUp.Id, order.Id, (int?)ex.ProviderStatusCode);
            }

            await _notifications.UpdateAsync(followUp, ct);
        }
    }

    /// <summary>
    /// Best-effort refresh of each notification's delivery outcome from the provider (there is
    /// no callback URL, so outcomes are obtained by asking). Provider failures leave the last
    /// known status in place.
    /// </summary>
    public async Task RefreshOutcomesAsync(IEnumerable<OrderNotification> notifications, CancellationToken ct)
    {
        foreach (var notification in notifications)
        {
            if (notification.ProviderMessageSid is null || TerminalStatuses.Contains(notification.Status))
            {
                continue;
            }

            try
            {
                var outcome = await _messaging.GetMessageAsync(notification.ProviderMessageSid, ct);
                notification.UpdateProviderOutcome(outcome.Status, outcome.ErrorCode, outcome.ErrorMessage);
                await _notifications.UpdateAsync(notification, ct);
            }
            catch (MessagingException ex)
            {
                _logger.LogWarning(ex, "Outcome refresh for notification {NotificationId} failed (provider status {ProviderStatus}).",
                    notification.Id, (int?)ex.ProviderStatusCode);
            }
        }
    }
}
