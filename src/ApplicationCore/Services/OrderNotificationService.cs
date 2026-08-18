using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Sends and tracks the SMS notifications tied to an order's lifecycle. Every send is recorded first,
/// so even if the provider call fails the record survives and the underlying operation is untouched.
/// Shopper numbers and message bodies are never logged.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How far after dispatch the "how did the delivery go?" follow-up is queued (a few days).</summary>
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    // Delivery outcomes that will not change again, so there is no point re-reading them from the provider.
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered", "undelivered", "failed", "canceled", "read", Notification.SendFailedStatus
    };

    private readonly IRepository<Notification> _notifications;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ISmsProvider _smsProvider;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Notification> notifications,
        IRepository<ContactNumber> contactNumbers,
        ISmsProvider smsProvider,
        IAppLogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _smsProvider = smsProvider;
        _logger = logger;
    }

    // --------------------------------------------------------------- lifecycle notifications

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default) =>
        SendImmediateToAllAsync(order, NotificationType.OrderPlaced, BuildBody(NotificationType.OrderPlaced, order), cancellationToken);

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await SendImmediateToAllAsync(order, NotificationType.OrderDispatched, BuildBody(NotificationType.OrderDispatched, order), cancellationToken);
        await ScheduleFollowUpToAllAsync(order, cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        // Call off the follow-up FIRST: a "how did delivery go?" for a cancelled order is the incident to prevent.
        await CancelPendingFollowUpsAsync(order, cancellationToken);
        await SendImmediateToAllAsync(order, NotificationType.OrderCancelled, BuildBody(NotificationType.OrderCancelled, order), cancellationToken);
    }

    private async Task SendImmediateToAllAsync(Order order, NotificationType type, string body, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
        if (numbers.Count == 0)
        {
            // A shopper with no number on file is simply not messaged.
            _logger.LogInformation("Order {OrderId}: no contact number on file; nothing sent for {Type}.", order.Id, type);
            return;
        }

        foreach (var number in numbers)
        {
            var notification = Notification.ForImmediate(order.Id, order.BuyerId, type, number.E164Number, body);
            await _notifications.AddAsync(notification, cancellationToken);
            await TrySendAsync(notification, cancellationToken);
        }
    }

    private async Task ScheduleFollowUpToAllAsync(Order order, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
        if (numbers.Count == 0)
            return;

        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var body = BuildBody(NotificationType.DeliveryFollowUp, order);

        foreach (var number in numbers)
        {
            var notification = Notification.ForScheduled(order.Id, order.BuyerId, NotificationType.DeliveryFollowUp, number.E164Number, body, sendAt);
            await _notifications.AddAsync(notification, cancellationToken);

            try
            {
                var result = await _smsProvider.ScheduleAsync(number.E164Number, body, sendAt, cancellationToken);
                notification.RecordProviderResult(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage);
                _logger.LogInformation("Order {OrderId}: follow-up scheduled ({Sid}) for a few days out.", order.Id, result.Sid);
            }
            catch (Exception ex)
            {
                notification.RecordSendFailure(Describe(ex));
                _logger.LogWarning("Order {OrderId}: follow-up could not be scheduled.", order.Id);
            }

            await _notifications.UpdateAsync(notification, cancellationToken);
        }
    }

    private async Task CancelPendingFollowUpsAsync(Order order, CancellationToken cancellationToken)
    {
        var notifications = await _notifications.ListAsync(new NotificationsByOrderSpecification(order.Id), cancellationToken);
        var pending = notifications.Where(n =>
            n.Type == NotificationType.DeliveryFollowUp &&
            !string.IsNullOrEmpty(n.ProviderMessageSid) &&
            string.Equals(n.ProviderStatus, "scheduled", StringComparison.OrdinalIgnoreCase));

        foreach (var followUp in pending)
        {
            try
            {
                await _smsProvider.CancelScheduledAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.UpdateDeliveryState("canceled", null, null);
                _logger.LogInformation("Order {OrderId}: pending follow-up {Sid} called off.", order.Id, followUp.ProviderMessageSid);
            }
            catch (Exception)
            {
                // The follow-up must never reach the shopper. Re-read the provider's state to see where it stands.
                try
                {
                    var state = await _smsProvider.FetchStateAsync(followUp.ProviderMessageSid!, cancellationToken);
                    followUp.UpdateDeliveryState(state.Status, state.ErrorCode, state.ErrorMessage);
                }
                catch (Exception)
                {
                    // ignore — recorded below
                }
                _logger.LogWarning("Order {OrderId}: attempt to cancel follow-up {Sid} did not confirm; current status {Status}.",
                    order.Id, followUp.ProviderMessageSid, followUp.ProviderStatus);
            }

            await _notifications.UpdateAsync(followUp, cancellationToken);
        }
    }

    private async Task TrySendAsync(Notification notification, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _smsProvider.SendAsync(notification.ToNumber, notification.Body!, cancellationToken);
            notification.RecordProviderResult(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage);
            _logger.LogInformation("Order {OrderId}: {Type} message accepted ({Sid}, {Status}).",
                notification.OrderId, notification.Type, result.Sid, result.Status);
        }
        catch (Exception ex)
        {
            // A message that cannot be sent must never fail the underlying operation.
            notification.RecordSendFailure(Describe(ex));
            _logger.LogWarning("Order {OrderId}: {Type} message could not be sent.", notification.OrderId, notification.Type);
        }

        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    // --------------------------------------------------------------- reads / refresh

    public async Task<IReadOnlyList<Notification>> GetNotificationsForOrderAsync(int orderId, bool refresh = true, CancellationToken cancellationToken = default)
    {
        var notifications = await _notifications.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        if (refresh)
            await RefreshStatusesAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task RefreshStatusesAsync(IReadOnlyCollection<Notification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
                continue;
            if (!string.IsNullOrEmpty(notification.ProviderStatus) && TerminalStatuses.Contains(notification.ProviderStatus!))
                continue;

            try
            {
                var state = await _smsProvider.FetchStateAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.UpdateDeliveryState(state.Status, state.ErrorCode, state.ErrorMessage);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception)
            {
                _logger.LogWarning("Could not refresh delivery state for notification {Id}.", notification.Id);
            }
        }
    }

    public Task<Notification?> GetByIdAsync(int notificationId, CancellationToken cancellationToken = default) =>
        _notifications.GetByIdAsync(notificationId, cancellationToken);

    // --------------------------------------------------------------- operator actions

    public async Task<ResendResult?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // Idempotency: a repeat under the same key returns the message the first attempt produced — no second send.
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var priorForKey = await _notifications.FirstOrDefaultAsync(new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
            if (priorForKey != null)
                return new ResendResult(priorForKey, MessageSent: false);
        }

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (source == null)
            return null;

        // Content that has been disposed of cannot be resent; there is nothing to send.
        if (string.IsNullOrEmpty(source.Body))
            return new ResendResult(source, MessageSent: false);

        var resend = Notification.ForImmediate(source.OrderId, source.BuyerId, source.Type, source.ToNumber, source.Body,
            idempotencyKey: string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey);
        await _notifications.AddAsync(resend, cancellationToken);
        await TrySendAsync(resend, cancellationToken);

        return new ResendResult(resend, MessageSent: true);
    }

    public async Task<Notification?> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification == null)
            return null;

        if (notification.ContentRedacted)
            return notification;

        // Dispose of the content at the provider first; only claim success locally once that succeeds.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
            await _smsProvider.RedactContentAsync(notification.ProviderMessageSid!, cancellationToken);

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Notification {Id}: content disposed of.", notification.Id);
        return notification;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _smsProvider.ListSentMessagesAsync(from, to, cancellationToken);
        var providerBySid = providerMessages
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First());

        // eShop's side: messages this app sent immediately from its configured number within the window.
        // (Scheduled follow-ups go out via the Messaging Service — a different sender — so they are not part of
        // the configured-number reconciliation.)
        var all = await _notifications.ListAsync(cancellationToken);
        var eShopInRange = all.Where(n =>
                n.ScheduledSendAt == null &&
                n.CreatedAt >= from && n.CreatedAt <= to)
            .ToList();

        var matched = new List<ReconciliationEntry>();
        var eShopOnly = new List<ReconciliationEntry>();
        var seenSids = new HashSet<string>();

        foreach (var n in eShopInRange)
        {
            if (!string.IsNullOrEmpty(n.ProviderMessageSid) && providerBySid.TryGetValue(n.ProviderMessageSid!, out var pm))
            {
                seenSids.Add(n.ProviderMessageSid!);
                matched.Add(new ReconciliationEntry(n.ProviderMessageSid, n.Id, pm.Status, n.ProviderStatus));
            }
            else
            {
                // eShop believes it sent this, but the provider's record for the configured number doesn't show it.
                eShopOnly.Add(new ReconciliationEntry(n.ProviderMessageSid, n.Id, null, n.ProviderStatus));
            }
        }

        var providerOnly = providerBySid.Values
            .Where(pm => !seenSids.Contains(pm.Sid))
            .Select(pm => new ReconciliationEntry(pm.Sid, null, pm.Status, null))
            .ToList();

        return new ReconciliationReport(
            from, to,
            _configuredFromNumberForReport(providerMessages),
            providerMessages.Count,
            eShopInRange.Count,
            matched,
            providerOnly,
            eShopOnly);
    }

    // The report echoes the sender it reconciled on. We take it from the provider records when present,
    // falling back to empty (the provider filtered on the configured number regardless).
    private static string _configuredFromNumberForReport(IReadOnlyList<ProviderMessageRecord> providerMessages) =>
        providerMessages.FirstOrDefault(m => !string.IsNullOrEmpty(m.From))?.From ?? string.Empty;

    // --------------------------------------------------------------- message bodies

    private static string BuildBody(NotificationType type, Order order) => type switch
    {
        NotificationType.OrderPlaced =>
            $"eShopOnWeb: thanks! Your order #{order.Id} has been placed" + TotalSuffix(order) + ".",
        NotificationType.OrderDispatched =>
            $"eShopOnWeb: good news — your order #{order.Id} is on its way!",
        NotificationType.OrderCancelled =>
            $"eShopOnWeb: your order #{order.Id} has been cancelled. If this is unexpected, please contact support.",
        NotificationType.DeliveryFollowUp =>
            $"eShopOnWeb: how did the delivery of your order #{order.Id} go? We'd love your feedback.",
        _ => $"eShopOnWeb: an update about your order #{order.Id}."
    };

    private static string TotalSuffix(Order order)
    {
        try
        {
            return $" (total {order.Total().ToString("C", CultureInfo.GetCultureInfo("en-US"))})";
        }
        catch
        {
            return string.Empty;
        }
    }

    // Keep provider/exception detail free of any shopper number or message body.
    private static string Describe(Exception ex) => ex.Message;
}
