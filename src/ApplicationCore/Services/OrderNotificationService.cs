using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.ApplicationCore.Sms;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    private readonly IRepository<SmsNotification> _notifications;
    private readonly IReadRepository<ContactNumber> _contactNumbers;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<OrderNotificationService> _logger;
    private readonly NotificationOptions _options;

    public OrderNotificationService(
        IRepository<SmsNotification> notifications,
        IReadRepository<ContactNumber> contactNumbers,
        ISmsGateway smsGateway,
        IAppLogger<OrderNotificationService> logger,
        IOptions<NotificationOptions> options)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _smsGateway = smsGateway;
        _logger = logger;
        _options = options.Value;
    }

    // ---- Message text (never contains the shopper's number) ---------------------------------

    private static string PlacedBody(Order o) => $"eShopOnWeb: thanks! Your order #{o.Id} has been placed. Total {o.Total():C}.";
    private static string DispatchedBody(Order o) => $"eShopOnWeb: good news - your order #{o.Id} is on its way!";
    private static string FollowUpBody(Order o) => $"eShopOnWeb: how did the delivery of order #{o.Id} go? Reply to let us know - we'd love your feedback.";
    private static string CancelledBody(Order o) => $"eShopOnWeb: your order #{o.Id} has been cancelled. If this is unexpected, please get in touch.";

    // ---- Order transitions ------------------------------------------------------------------

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var numbers = await GetBuyerNumbersAsync(order.BuyerId, cancellationToken);
        foreach (var number in numbers)
            await SendAndRecordAsync(order, number, NotificationKind.OrderPlaced, PlacedBody(order), null, null, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var numbers = await GetBuyerNumbersAsync(order.BuyerId, cancellationToken);
        var followUpAt = DateTimeOffset.UtcNow.Add(_options.FollowUpDelay);
        foreach (var number in numbers)
        {
            // Tell them it's on its way...
            await SendAndRecordAsync(order, number, NotificationKind.OrderDispatched, DispatchedBody(order), null, null, cancellationToken);
            // ...and queue the delivery follow-up with the provider for a few days later.
            await SendAndRecordAsync(order, number, NotificationKind.DeliveryFollowUp, FollowUpBody(order), followUpAt, null, cancellationToken);
        }
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        // Call off any follow-up that has not yet gone out, so a cancelled order never gets a "how did delivery go?".
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        var numbers = await GetBuyerNumbersAsync(order.BuyerId, cancellationToken);
        foreach (var number in numbers)
            await SendAndRecordAsync(order, number, NotificationKind.OrderCancelled, CancelledBody(order), null, null, cancellationToken);
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _notifications.ListAsync(new OrderNotificationsSpecification(orderId), cancellationToken);
        foreach (var n in notifications)
        {
            var isPendingFollowUp = n.Kind == NotificationKind.DeliveryFollowUp
                && n.IsScheduled
                && !string.IsNullOrEmpty(n.ProviderSid)
                && string.Equals(n.Status, SmsStatuses.Scheduled, StringComparison.OrdinalIgnoreCase);
            if (!isPendingFollowUp)
                continue;

            try
            {
                await _smsGateway.CancelScheduledAsync(n.ProviderSid!, cancellationToken);
                n.UpdateStatus(SmsStatuses.Canceled);
                await _notifications.UpdateAsync(n, cancellationToken);
                _logger.LogInformation("Cancelled scheduled follow-up (notification {Id}) for order {OrderId}.", n.Id, orderId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to cancel scheduled follow-up (notification {Id}) for order {OrderId}: {Error}", n.Id, orderId, ex.Message);
            }
        }
    }

    // ---- Reads ------------------------------------------------------------------------------

    public async Task<IReadOnlyList<SmsNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notifications.ListAsync(new OrderNotificationsSpecification(orderId), cancellationToken);
        foreach (var n in notifications)
            await RefreshStatusAsync(n, cancellationToken);
        return notifications;
    }

    private async Task RefreshStatusAsync(SmsNotification n, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(n.ProviderSid) || SmsStatuses.IsTerminal(n.Status))
            return;
        try
        {
            var status = await _smsGateway.FetchStatusAsync(n.ProviderSid!, cancellationToken);
            n.UpdateStatus(status.Status, status.ErrorCode);
            await _notifications.UpdateAsync(n, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not refresh status for notification {Id} (order {OrderId}): {Error}", n.Id, n.OrderId, ex.Message);
        }
    }

    // ---- Operator levers --------------------------------------------------------------------

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return ResendResult.Invalid(notificationId, "An idempotency key is required.");

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
            return ResendResult.NotFound();

        // Repeating the request under the same key must not send again.
        var priorForKey = await _notifications.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (priorForKey is not null)
            return ResendResult.Duplicate(priorForKey.Id);

        if (string.IsNullOrEmpty(original.Body))
            return ResendResult.Invalid(notificationId, "The message content has been disposed of and cannot be resent.");

        var resend = new SmsNotification(original.BuyerId, original.OrderId, NotificationKind.Resend,
            original.ToNumber, original.Body!, isScheduled: false, idempotencyKey: idempotencyKey);
        await _notifications.AddAsync(resend, cancellationToken);
        await TrySendAsync(resend, null, cancellationToken);
        return ResendResult.Fresh(resend.Id);
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
            return false;

        // Dispose at the provider first so its text is unrecoverable there too; the record + status survive.
        if (!string.IsNullOrEmpty(notification.ProviderSid))
            await _smsGateway.RedactContentAsync(notification.ProviderSid!, cancellationToken);

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Disposed of content for notification {Id} (order {OrderId}).", notification.Id, notification.OrderId);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
    {
        // The provider's own record for eShop's sending number, across the whole range.
        var providerMessages = await _smsGateway.ListSentMessagesAsync(fromUtc, toUtc, cancellationToken);
        var providerBySid = providerMessages
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // What eShop believes it sent in the range.
        var allLocal = await _notifications.ListAsync(cancellationToken);
        var localInRange = allLocal.Where(n => n.CreatedAt >= fromUtc && n.CreatedAt <= toUtc).ToList();
        var localBySid = localInRange
            .Where(n => !string.IsNullOrEmpty(n.ProviderSid))
            .GroupBy(n => n.ProviderSid!)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var entries = new List<ReconciliationEntry>();
        int matched = 0, providerOnly = 0, eShopOnly = 0;

        // Provider side: matched vs. provider-only.
        foreach (var pm in providerMessages)
        {
            if (localBySid.TryGetValue(pm.Sid, out var local))
            {
                matched++;
                entries.Add(new ReconciliationEntry(pm.Sid, ReconciliationDiscrepancy.Matched,
                    pm.Status, local.Status, local.OrderId, local.Kind, pm.DateSent));
            }
            else
            {
                providerOnly++;
                entries.Add(new ReconciliationEntry(pm.Sid, ReconciliationDiscrepancy.ProviderOnly,
                    pm.Status, null, null, null, pm.DateSent));
            }
        }

        // eShop side: anything eShop believes it sent that the provider did not report in the range.
        foreach (var local in localInRange)
        {
            var known = !string.IsNullOrEmpty(local.ProviderSid) && providerBySid.ContainsKey(local.ProviderSid!);
            if (known)
                continue;
            eShopOnly++;
            entries.Add(new ReconciliationEntry(local.ProviderSid, ReconciliationDiscrepancy.EShopOnly,
                null, local.Status, local.OrderId, local.Kind, local.CreatedAt));
        }

        return new ReconciliationReport(fromUtc, toUtc, providerMessages.Count, localInRange.Count,
            matched, providerOnly, eShopOnly, entries);
    }

    // ---- Helpers ----------------------------------------------------------------------------

    private async Task<IReadOnlyList<ContactNumber>> GetBuyerNumbersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        if (numbers.Count == 0)
            _logger.LogInformation("Buyer {BuyerId} has no contact number on file; nothing to send.", buyerId);
        return numbers;
    }

    private async Task SendAndRecordAsync(Order order, ContactNumber number, NotificationKind kind, string body,
        DateTimeOffset? scheduleAt, string? idempotencyKey, CancellationToken cancellationToken)
    {
        var notification = new SmsNotification(order.BuyerId, order.Id, kind, number.PhoneNumber, body,
            isScheduled: scheduleAt.HasValue, idempotencyKey: idempotencyKey);
        await _notifications.AddAsync(notification, cancellationToken);
        await TrySendAsync(notification, scheduleAt, cancellationToken);
    }

    /// <summary>
    /// Hand a persisted notification to the provider. A messaging failure is recorded on the
    /// notification and swallowed — it must never fail the underlying order operation.
    /// </summary>
    private async Task TrySendAsync(SmsNotification notification, DateTimeOffset? scheduleAt, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _smsGateway.SendAsync(notification.ToNumber, notification.Body!, scheduleAt, cancellationToken);
            notification.RecordAccepted(result.Sid, result.Status, result.ErrorCode);
        }
        catch (Exception ex)
        {
            notification.RecordSendError();
            _logger.LogWarning("SMS send failed for notification {Id} (order {OrderId}, kind {Kind}): {Error}",
                notification.Id, notification.OrderId, notification.Kind, ex.Message);
        }
        await _notifications.UpdateAsync(notification, cancellationToken);
    }
}
