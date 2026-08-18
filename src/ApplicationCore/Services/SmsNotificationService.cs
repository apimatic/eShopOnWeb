using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the SMS notifications that go out as an order moves, and the operator actions on
/// them. The three notify* methods are best-effort: they never throw to their caller, so the order
/// operation that triggered them always succeeds. A shopper with no number on file is not messaged.
/// </summary>
public class SmsNotificationService : ISmsNotificationService
{
    private readonly IRepository<SmsNotification> _notifications;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ITwilioMessagingClient _client;
    private readonly IAppLogger<SmsNotificationService> _logger;
    private readonly SmsNotificationOptions _options;

    public SmsNotificationService(
        IRepository<SmsNotification> notifications,
        IRepository<ContactNumber> contactNumbers,
        ITwilioMessagingClient client,
        IAppLogger<SmsNotificationService> logger,
        IOptions<SmsNotificationOptions> options)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _client = client;
        _logger = logger;
        _options = options.Value;
    }

    // ---- Message bodies -------------------------------------------------------------------------

    private static string PlacedBody(int orderId) =>
        $"eShop: your order #{orderId} has been placed. We'll text you when it ships. Thank you!";

    private static string DispatchedBody(int orderId) =>
        $"eShop: good news — your order #{orderId} is on its way!";

    private static string FollowUpBody(int orderId) =>
        $"eShop: how did the delivery of your order #{orderId} go? We'd love your feedback.";

    private static string CancelledBody(int orderId) =>
        $"eShop: your order #{orderId} has been cancelled. If this is unexpected, please contact support.";

    private static string ResendBody(int orderId) =>
        $"eShop: an update about your order #{orderId}.";

    // ---- Notify on order movement ---------------------------------------------------------------

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(order, nameof(order));
        await SafeAsync(async () =>
        {
            var numbers = await ActiveNumbersAsync(order.BuyerId, order.Id, NotificationKind.OrderPlaced, cancellationToken);
            var body = PlacedBody(order.Id);
            foreach (var number in numbers)
            {
                await SendImmediateAsync(order, NotificationKind.OrderPlaced, body, number, cancellationToken);
            }
        }, order.Id, NotificationKind.OrderPlaced);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(order, nameof(order));
        await SafeAsync(async () =>
        {
            var numbers = await ActiveNumbersAsync(order.BuyerId, order.Id, NotificationKind.OrderDispatched, cancellationToken);
            var dispatchBody = DispatchedBody(order.Id);
            var followUpBody = FollowUpBody(order.Id);
            var sendAtUtc = DateTimeOffset.UtcNow.Add(_options.FollowUpDelay);

            foreach (var number in numbers)
            {
                await SendImmediateAsync(order, NotificationKind.OrderDispatched, dispatchBody, number, cancellationToken);
                await ScheduleFollowUpAsync(order, followUpBody, number, sendAtUtc, cancellationToken);
            }
        }, order.Id, NotificationKind.OrderDispatched);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(order, nameof(order));
        await SafeAsync(async () =>
        {
            // Call off any not-yet-sent follow-up first, so a "how did delivery go?" message can
            // never reach the customer for an order that was cancelled.
            await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

            var numbers = await ActiveNumbersAsync(order.BuyerId, order.Id, NotificationKind.OrderCancelled, cancellationToken);
            var body = CancelledBody(order.Id);
            foreach (var number in numbers)
            {
                await SendImmediateAsync(order, NotificationKind.OrderCancelled, body, number, cancellationToken);
            }
        }, order.Id, NotificationKind.OrderCancelled);
    }

    private async Task<IReadOnlyList<ContactNumber>> ActiveNumbersAsync(string buyerId, int orderId, NotificationKind kind, CancellationToken ct)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
        if (numbers.Count == 0)
        {
            _logger.LogInformation("No contact number on file for the buyer of order {0}; skipping {1} notification.", orderId, kind);
        }

        return numbers;
    }

    private async Task SendImmediateAsync(Order order, NotificationKind kind, string body, ContactNumber number, CancellationToken ct)
    {
        var notification = new SmsNotification(order.BuyerId, order.Id, kind, number.E164Number, body);
        await _notifications.AddAsync(notification, ct);

        try
        {
            var message = await _client.SendAsync(number.E164Number, body, ct);
            notification.RecordAccepted(message.Sid, message.Status);
            _logger.LogInformation("{0} notification for order {1} accepted by provider ({2}).", kind, order.Id, message.Sid);
        }
        catch (Exception ex)
        {
            notification.RecordSendFailure(ex.Message);
            _logger.LogWarning("{0} notification for order {1} could not be sent: {2}", kind, order.Id, ex.Message);
        }

        await _notifications.UpdateAsync(notification, ct);
    }

    private async Task ScheduleFollowUpAsync(Order order, string body, ContactNumber number, DateTimeOffset sendAtUtc, CancellationToken ct)
    {
        var notification = new SmsNotification(
            order.BuyerId, order.Id, NotificationKind.DeliveryFollowUp, number.E164Number, body,
            isFollowUp: true, scheduledForUtc: sendAtUtc);
        await _notifications.AddAsync(notification, ct);

        try
        {
            var message = await _client.ScheduleAsync(number.E164Number, body, sendAtUtc, ct);
            notification.RecordAccepted(message.Sid, message.Status);
            _logger.LogInformation("Delivery follow-up for order {0} scheduled with provider ({1}) for {2:o}.", order.Id, message.Sid, sendAtUtc);
        }
        catch (Exception ex)
        {
            notification.RecordSendFailure(ex.Message);
            _logger.LogWarning("Delivery follow-up for order {0} could not be scheduled: {1}", order.Id, ex.Message);
        }

        await _notifications.UpdateAsync(notification, ct);
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken ct)
    {
        var pending = await _notifications.ListAsync(new PendingFollowUpsByOrderSpecification(orderId), ct);
        foreach (var followUp in pending)
        {
            if (followUp.ProviderMessageSid is null)
            {
                // Never accepted by the provider, so nothing is queued to go out. Reflect the cancel.
                followUp.MarkScheduledCancelled();
                await _notifications.UpdateAsync(followUp, ct);
                continue;
            }

            try
            {
                await _client.CancelScheduledAsync(followUp.ProviderMessageSid, ct);
                followUp.MarkScheduledCancelled();
                await _notifications.UpdateAsync(followUp, ct);
                _logger.LogInformation("Called off scheduled follow-up {0} for order {1}.", followUp.Id, orderId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not call off scheduled follow-up {0} for order {1}: {2}", followUp.Id, orderId, ex.Message);
            }
        }
    }

    // ---- Reads ----------------------------------------------------------------------------------

    public async Task<IReadOnlyList<SmsNotification>> GetOrderNotificationsAsync(int orderId, bool refresh = true, CancellationToken cancellationToken = default)
    {
        var notifications = await _notifications.ListAsync(new SmsNotificationsByOrderSpecification(orderId), cancellationToken);
        if (refresh)
        {
            await RefreshFromProviderAsync(notifications, cancellationToken);
        }

        return notifications;
    }

    public async Task<IReadOnlyDictionary<int, List<SmsNotification>>> GetNotificationsForOrdersAsync(IReadOnlyCollection<int> orderIds, bool refresh = true, CancellationToken cancellationToken = default)
    {
        if (orderIds.Count == 0)
        {
            return new Dictionary<int, List<SmsNotification>>();
        }

        var notifications = await _notifications.ListAsync(new SmsNotificationsByOrdersSpecification(orderIds), cancellationToken);
        if (refresh)
        {
            await RefreshFromProviderAsync(notifications, cancellationToken);
        }

        return notifications
            .GroupBy(n => n.OrderId)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    public Task<SmsNotification?> GetByIdAsync(int notificationId, CancellationToken cancellationToken = default) =>
        _notifications.GetByIdAsync(notificationId, cancellationToken);

    /// <summary>
    /// Reads the latest delivery outcome back from the provider for any non-terminal message, so a
    /// read reflects where notifications actually got to. Never throws.
    /// </summary>
    private async Task RefreshFromProviderAsync(IEnumerable<SmsNotification> notifications, CancellationToken ct)
    {
        foreach (var notification in notifications)
        {
            if (notification.ProviderMessageSid is null || NotificationDeliveryStatus.IsTerminal(notification.DeliveryStatus))
            {
                continue;
            }

            try
            {
                var message = await _client.FetchAsync(notification.ProviderMessageSid, ct);
                notification.ApplyProviderState(message.Status, message.ErrorCode, message.ErrorMessage);
                await _notifications.UpdateAsync(notification, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not refresh delivery status for notification {0}: {1}", notification.Id, ex.Message);
            }
        }
    }

    // ---- Operator actions -----------------------------------------------------------------------

    public async Task<SmsNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(idempotencyKey, nameof(idempotencyKey));

        // Repeating a request under the same key must not send a second message.
        var already = await _notifications.FirstOrDefaultAsync(
            new SmsNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (already is not null)
        {
            _logger.LogInformation("Resend under idempotency key already satisfied by notification {0}.", already.Id);
            return already;
        }

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (source is null)
        {
            return null;
        }

        // Re-send the original content where it survives; if it was disposed, send a neutral notice.
        var body = string.IsNullOrEmpty(source.Body) ? ResendBody(source.OrderId) : source.Body!;

        var resend = new SmsNotification(
            source.BuyerId, source.OrderId, NotificationKind.Resend, source.ToNumber, body,
            idempotencyKey: idempotencyKey, resendOfNotificationId: source.Id);
        await _notifications.AddAsync(resend, cancellationToken);

        try
        {
            var message = await _client.SendAsync(source.ToNumber, body, cancellationToken);
            resend.RecordAccepted(message.Sid, message.Status);
            _logger.LogInformation("Resend of notification {0} accepted by provider ({1}).", source.Id, message.Sid);
        }
        catch (Exception ex)
        {
            resend.RecordSendFailure(ex.Message);
            _logger.LogWarning("Resend of notification {0} could not be sent: {1}", source.Id, ex.Message);
        }

        await _notifications.UpdateAsync(resend, cancellationToken);
        return resend;
    }

    public async Task<SmsNotification?> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return null;
        }

        // The text must no longer be retrievable from the provider either — so redact there first.
        // If that cannot be done we do NOT claim disposal; the failure propagates to the caller.
        if (notification.ProviderMessageSid is not null)
        {
            await _client.RedactContentAsync(notification.ProviderMessageSid, cancellationToken);
        }

        notification.MarkContentDisposed();
        await _notifications.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Content disposed for notification {0}.", notification.Id);
        return notification;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _client.ListSentMessagesAsync(from, to, cancellationToken);
        var eShopNotifications = await _notifications.ListAsync(
            new SmsNotificationsWithProviderIdBetweenSpecification(from, to), cancellationToken);

        var eShopBySid = new Dictionary<string, SmsNotification>();
        foreach (var n in eShopNotifications)
        {
            if (n.ProviderMessageSid is not null)
            {
                eShopBySid[n.ProviderMessageSid] = n;
            }
        }

        var providerBySid = new Dictionary<string, ProviderMessage>();
        foreach (var m in providerMessages)
        {
            providerBySid[m.Sid] = m;
        }

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var eShopOnly = new List<ReconciliationEntry>();

        foreach (var (sid, message) in providerBySid)
        {
            if (eShopBySid.TryGetValue(sid, out var notification))
            {
                matched.Add(new ReconciliationEntry(
                    sid, notification.Id, notification.OrderId, message.Status, notification.DeliveryStatus,
                    PhoneNumberMasking.Mask(message.To ?? notification.ToNumber), message.DateSentUtc));
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry(
                    sid, null, null, message.Status, null, PhoneNumberMasking.Mask(message.To), message.DateSentUtc));
            }
        }

        foreach (var (sid, notification) in eShopBySid)
        {
            if (!providerBySid.ContainsKey(sid))
            {
                eShopOnly.Add(new ReconciliationEntry(
                    sid, notification.Id, notification.OrderId, null, notification.DeliveryStatus,
                    PhoneNumberMasking.Mask(notification.ToNumber), null));
            }
        }

        return new ReconciliationReport(from, to, matched, providerOnly, eShopOnly);
    }

    // ---- Helpers --------------------------------------------------------------------------------

    /// <summary>Runs a notify action, swallowing any failure so the order operation still succeeds.</summary>
    private async Task SafeAsync(Func<Task> action, int orderId, NotificationKind kind)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Notification work ({0}) for order {1} failed but the order operation is unaffected: {2}", kind, orderId, ex.Message);
        }
    }
}
