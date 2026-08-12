using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Orchestrates order SMS notifications over the Twilio messaging client. Sends are
/// best-effort: every "notify" path swallows provider/transport failures (recording them on
/// the notification) so the underlying order operation always succeeds. A shopper with no
/// number on file is simply not messaged.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How long after dispatch the "how did delivery go?" follow-up is queued for.</summary>
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered", "undelivered", "failed", "canceled", "received", "read",
        OrderNotification.StatusSendFailed
    };

    // Provider statuses for a scheduled message that has not yet gone out and can be called off.
    private static readonly HashSet<string> CancellableStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "scheduled", "accepted", "queued"
    };

    private readonly ITwilioMessagingClient _messagingClient;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly TwilioSettings _settings;
    private readonly ILogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        ITwilioMessagingClient messagingClient,
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        IOptions<TwilioSettings> settings,
        ILogger<OrderNotificationService> logger)
    {
        _messagingClient = messagingClient;
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var numbers = await GetContactNumbersAsync(order.BuyerId, cancellationToken).ConfigureAwait(false);
        foreach (var number in numbers)
        {
            await SendImmediateAsync(order, NotificationKind.OrderPlaced,
                number, BodyFor(NotificationKind.OrderPlaced, order.Id), cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var numbers = await GetContactNumbersAsync(order.BuyerId, cancellationToken).ConfigureAwait(false);
        foreach (var number in numbers)
        {
            await SendImmediateAsync(order, NotificationKind.OrderDispatched,
                number, BodyFor(NotificationKind.OrderDispatched, order.Id), cancellationToken).ConfigureAwait(false);

            // Queue the follow-up with the provider itself — this app holds no timer.
            await ScheduleFollowUpAsync(order, number, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        // First, call off any follow-up that has not yet gone out — independent of whether the
        // shopper still has a number on file. Asking how a cancelled delivery went is the
        // incident this prevents.
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken).ConfigureAwait(false);

        var numbers = await GetContactNumbersAsync(order.BuyerId, cancellationToken).ConfigureAwait(false);
        foreach (var number in numbers)
        {
            await SendImmediateAsync(order, NotificationKind.OrderCancelled,
                number, BodyFor(NotificationKind.OrderCancelled, order.Id), cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken).ConfigureAwait(false);
        if (original is null)
            return null;

        // Idempotency: a repeat under the same key returns the notification the first attempt
        // produced, without sending again. A fresh key is a genuine new attempt.
        var existing = await _notificationRepository
            .FirstOrDefaultAsync(new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            _logger.LogInformation("Resend request for notification {NotificationId} matched idempotency key; no new message sent.", notificationId);
            return existing;
        }

        var body = original.Body ?? BodyFor(original.Kind, original.OrderId);
        var resend = new OrderNotification(
            original.OrderId, original.BuyerId, original.Kind, original.ToPhoneNumber, body,
            idempotencyKey: idempotencyKey, resendOfNotificationId: original.Id);
        await _notificationRepository.AddAsync(resend, cancellationToken).ConfigureAwait(false);

        await DispatchSendAsync(resend, () => _messagingClient.SendMessageAsync(original.ToPhoneNumber, body, cancellationToken),
            NotificationKind.OrderPlaced /* label unused */, original.OrderId, cancellationToken).ConfigureAwait(false);

        return resend;
    }

    public async Task<bool> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken).ConfigureAwait(false);
        if (notification is null)
            return false;

        // If the message reached the provider, its text must be redacted there too — not merely
        // hidden locally. A provider failure here is surfaced (this is an operator action, not
        // an order operation), so we never falsely claim the content is gone.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            var updated = await _messagingClient
                .RedactMessageBodyAsync(notification.ProviderMessageSid!, cancellationToken)
                .ConfigureAwait(false);
            if (!string.IsNullOrEmpty(updated.Status))
                notification.UpdateDeliveryStatus(updated.Status!, updated.ErrorCode, updated.ErrorMessage);
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task RefreshStatusesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
                continue;
            if (TerminalStatuses.Contains(notification.Status))
                continue;

            try
            {
                var latest = await _messagingClient.FetchMessageAsync(notification.ProviderMessageSid!, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(latest.Status))
                {
                    notification.UpdateDeliveryStatus(latest.Status!, latest.ErrorCode, latest.ErrorMessage);
                    await _notificationRepository.UpdateAsync(notification, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A read must not fail because the provider was briefly unreachable.
                _logger.LogWarning("Could not refresh delivery status for notification {NotificationId}.", notification.Id);
            }
        }
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var report = new ReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = _settings.FromNumber
        };

        // Ask the provider only for our own sending number's messages.
        var providerMessages = await _messagingClient
            .ListMessagesByFromAsync(_settings.FromNumber, from, to, cancellationToken)
            .ConfigureAwait(false);

        // Keep the provider's record of messages actually sent within the exact instant range.
        var providerInRange = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid) && m.DateSent is not null && m.DateSent >= from && m.DateSent <= to)
            .GroupBy(m => m.Sid!)
            .Select(g => g.First())
            .ToDictionary(m => m.Sid!, StringComparer.OrdinalIgnoreCase);

        // eShop's record: notifications that reached the provider (have a SID) created in range.
        var localAll = await _notificationRepository
            .ListAsync(new OrderNotificationsInRangeSpecification(from, to), cancellationToken)
            .ConfigureAwait(false);

        // Bring eShop's side up to date with the provider so the report compares current truth.
        await RefreshStatusesAsync(localAll, cancellationToken).ConfigureAwait(false);

        var localBySid = localAll
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        report.ProviderCount = providerInRange.Count;
        report.EShopCount = localBySid.Count;

        foreach (var (sid, providerMsg) in providerInRange)
        {
            if (localBySid.TryGetValue(sid, out var local))
            {
                report.Matched.Add(new ReconciliationEntry
                {
                    MessageSid = sid,
                    NotificationId = local.Id,
                    OrderId = local.OrderId,
                    ProviderStatus = providerMsg.Status,
                    EShopStatus = local.Status,
                    ProviderDateSent = providerMsg.DateSent
                });
            }
            else
            {
                report.ProviderOnly.Add(new ReconciliationEntry
                {
                    MessageSid = sid,
                    ProviderStatus = providerMsg.Status,
                    ProviderDateSent = providerMsg.DateSent
                });
            }
        }

        foreach (var (sid, local) in localBySid)
        {
            if (!providerInRange.ContainsKey(sid))
            {
                report.EShopOnly.Add(new ReconciliationEntry
                {
                    MessageSid = sid,
                    NotificationId = local.Id,
                    OrderId = local.OrderId,
                    EShopStatus = local.Status
                });
            }
        }

        return report;
    }

    // ----- helpers -------------------------------------------------------------------

    private async Task<IReadOnlyList<string>> GetContactNumbersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumberRepository
            .ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken)
            .ConfigureAwait(false);
        return numbers.Select(n => n.PhoneNumber).ToList();
    }

    private async Task SendImmediateAsync(Order order, NotificationKind kind, string toNumber, string body, CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(order.Id, order.BuyerId, kind, toNumber, body);
        await _notificationRepository.AddAsync(notification, cancellationToken).ConfigureAwait(false);
        await DispatchSendAsync(notification, () => _messagingClient.SendMessageAsync(toNumber, body, cancellationToken),
            kind, order.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task ScheduleFollowUpAsync(Order order, string toNumber, CancellationToken cancellationToken)
    {
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var body = BodyFor(NotificationKind.DeliveryFollowUp, order.Id);
        var notification = new OrderNotification(order.Id, order.BuyerId, NotificationKind.DeliveryFollowUp,
            toNumber, body, scheduledSendAt: sendAt);
        await _notificationRepository.AddAsync(notification, cancellationToken).ConfigureAwait(false);
        await DispatchSendAsync(notification, () => _messagingClient.ScheduleMessageAsync(toNumber, body, sendAt, cancellationToken),
            NotificationKind.DeliveryFollowUp, order.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs a send/schedule, recording the provider result or the failure. Never throws.</summary>
    private async Task DispatchSendAsync(OrderNotification notification, Func<Task<TwilioMessage>> send,
        NotificationKind kind, int orderId, CancellationToken cancellationToken)
    {
        try
        {
            var msg = await send().ConfigureAwait(false);
            if (string.IsNullOrEmpty(msg.Sid))
            {
                notification.RecordSendFailure("Provider accepted the request but returned no message identifier.");
            }
            else
            {
                notification.RecordProviderAccepted(msg.Sid!, msg.Status ?? string.Empty, msg.ErrorCode, msg.ErrorMessage);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never propagate: the underlying order operation must still succeed.
            notification.RecordSendFailure(Sanitize(ex));
            _logger.LogWarning("Could not send {Kind} notification for order {OrderId}.", kind, orderId);
        }

        await _notificationRepository.UpdateAsync(notification, cancellationToken).ConfigureAwait(false);
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notificationRepository
            .ListAsync(new OrderNotificationsByOrderAndKindSpecification(orderId, NotificationKind.DeliveryFollowUp), cancellationToken)
            .ConfigureAwait(false);

        foreach (var followUp in followUps)
        {
            if (string.IsNullOrEmpty(followUp.ProviderMessageSid) || !CancellableStatuses.Contains(followUp.Status))
                continue;

            try
            {
                var updated = await _messagingClient.CancelScheduledMessageAsync(followUp.ProviderMessageSid!, cancellationToken).ConfigureAwait(false);
                followUp.UpdateDeliveryStatus(updated.Status ?? "canceled", updated.ErrorCode, updated.ErrorMessage);
                await _notificationRepository.UpdateAsync(followUp, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Cancellation must not fail the order cancel; record and move on.
                _logger.LogWarning("Could not cancel scheduled follow-up notification {NotificationId} for order {OrderId}.", followUp.Id, orderId);
            }
        }
    }

    private static string Sanitize(Exception ex)
    {
        if (ex is TwilioApiException tex)
            return $"Provider error (HTTP {(int)tex.HttpStatus}{(tex.TwilioCode is not null ? $", code {tex.TwilioCode}" : string.Empty)}).";
        return "The message could not be sent.";
    }

    private static string BodyFor(NotificationKind kind, int orderId) => kind switch
    {
        NotificationKind.OrderPlaced => $"eShopOnWeb: your order #{orderId} has been placed. Thank you for shopping with us!",
        NotificationKind.OrderDispatched => $"eShopOnWeb: good news — your order #{orderId} is on its way!",
        NotificationKind.DeliveryFollowUp => $"eShopOnWeb: how did the delivery of your order #{orderId} go? We'd love your feedback.",
        NotificationKind.OrderCancelled => $"eShopOnWeb: your order #{orderId} has been cancelled. Please get in touch with any questions.",
        _ => $"eShopOnWeb: an update about your order #{orderId}."
    };
}
