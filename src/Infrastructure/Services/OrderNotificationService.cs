using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.ApplicationCore.Sms;
using Microsoft.eShopWeb.Infrastructure.Sms;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Orchestrates the SMS notifications that accompany an order. The three order-event methods swallow every
/// messaging failure so the order still gets placed/dispatched/cancelled and the caller's request still
/// succeeds; the operator actions surface provider failures because sending IS the request.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    private const int MaxRefreshPerRequest = 100;

    private readonly ISmsGateway _gateway;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IReadRepository<ContactNumber> _contactNumbers;
    private readonly TwilioSettings _settings;
    private readonly ILogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        ISmsGateway gateway,
        IRepository<OrderNotification> notifications,
        IReadRepository<ContactNumber> contactNumbers,
        IOptions<TwilioSettings> settings,
        ILogger<OrderNotificationService> logger)
    {
        _gateway = gateway;
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken ct = default)
    {
        using var scope = BudgetScope(_settings.RequestBudgetSeconds, ct);
        var token = scope.Token;

        var toNumber = await ResolveNumberAsync(order.BuyerId, token);
        if (toNumber is null)
        {
            _logger.LogInformation("No contact number on file for order {OrderId}; order-placed message skipped.", order.Id);
            return;
        }

        var body = $"eShop: your order #{order.Id} has been placed. We'll text you when it ships.";
        await SendAndRecordAsync(order, toNumber, NotificationKind.OrderPlaced, body, token);
    }

    public async Task<OrderEventOutcome> NotifyOrderDispatchedAsync(Order order, CancellationToken ct = default)
    {
        using var scope = BudgetScope(_settings.RequestBudgetSeconds, ct);
        var token = scope.Token;

        var history = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(order.Id), token);
        if (history.Any(n => n.Kind == NotificationKind.OrderCancelled))
        {
            return OrderEventOutcome.AlreadyCancelled;
        }

        if (history.Any(n => n.Kind == NotificationKind.OrderDispatched))
        {
            return OrderEventOutcome.AlreadyDispatched;
        }

        var toNumber = await ResolveNumberAsync(order.BuyerId, token);
        if (toNumber is null)
        {
            _logger.LogInformation("No contact number on file for order {OrderId}; dispatch messages skipped.", order.Id);
            return OrderEventOutcome.Notified;
        }

        var dispatchBody = $"eShop: your order #{order.Id} is on its way!";
        await SendAndRecordAsync(order, toNumber, NotificationKind.OrderDispatched, dispatchBody, token);

        // Queue the "how did delivery go?" follow-up WITH THE PROVIDER for a few days later — the provider
        // holds and sends it, not this application.
        var sendAt = DateTimeOffset.UtcNow.AddDays(_settings.FollowUpDelayDays);
        var followUpBody = $"eShop: how did the delivery of your order #{order.Id} go? We'd love your feedback.";
        var followUp = new OrderNotification(order.Id, order.BuyerId, toNumber, NotificationKind.DeliveryFollowUp, followUpBody);
        try
        {
            var scheduled = await _gateway.ScheduleAsync(toNumber, followUpBody, sendAt, token);
            followUp.RecordScheduled(scheduled.ProviderMessageSid, scheduled.Status, scheduled.ScheduledSendAt ?? sendAt);
        }
        catch (SmsGatewayException ex)
        {
            followUp.RecordSendFailed(ex.Message);
            _logger.LogError("Failed to schedule delivery follow-up for order {OrderId} ({Kind}).", order.Id, ex.Kind);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            followUp.RecordSendFailed("Unexpected error scheduling the follow-up.");
            _logger.LogError(ex, "Unexpected error scheduling delivery follow-up for order {OrderId}.", order.Id);
        }

        await _notifications.AddAsync(followUp, token);
        return OrderEventOutcome.Notified;
    }

    public async Task<OrderEventOutcome> NotifyOrderCancelledAsync(Order order, CancellationToken ct = default)
    {
        using var scope = BudgetScope(_settings.RequestBudgetSeconds, ct);
        var token = scope.Token;

        var history = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(order.Id), token);
        if (history.Any(n => n.Kind == NotificationKind.OrderCancelled))
        {
            return OrderEventOutcome.AlreadyCancelled;
        }

        // Call off any delivery follow-up still held by the provider FIRST — asking a customer how their
        // delivery went for an order that was cancelled is exactly the incident this prevents.
        await CancelPendingFollowUpsAsync(order.Id, token);

        var toNumber = await ResolveNumberAsync(order.BuyerId, token);
        if (toNumber is null)
        {
            _logger.LogInformation("No contact number on file for order {OrderId}; cancellation message skipped.", order.Id);
            return OrderEventOutcome.Notified;
        }

        var body = $"eShop: your order #{order.Id} has been cancelled. If this is unexpected, please contact support.";
        await SendAndRecordAsync(order, toNumber, NotificationKind.OrderCancelled, body, token);
        return OrderEventOutcome.Notified;
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct = default)
    {
        using var scope = BudgetScope(_settings.RequestBudgetSeconds, ct);
        var token = scope.Token;

        // Idempotency: a repeat under the same key returns the first result without sending again.
        var existing = await _notifications.FirstOrDefaultAsync(new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), token);
        if (existing is not null)
        {
            return ResendResult.Sent(existing);
        }

        var source = await _notifications.GetByIdAsync(notificationId, token);
        if (source is null)
        {
            return ResendResult.SourceNotFound();
        }

        // Nothing may be sent to a number the shopper has removed from their file.
        var stillOnFile = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByOwnerAndValueSpecification(source.OwnerId, source.ToNumber), token);
        if (stillOnFile is null)
        {
            return ResendResult.NumberNoLongerOnFile();
        }

        var body = source.Body ?? $"eShop: an update about your order #{source.OrderId}.";
        var resend = new OrderNotification(source.OrderId, source.OwnerId, source.ToNumber, NotificationKind.Resend, body);
        resend.SetIdempotencyKey(idempotencyKey);

        try
        {
            var result = await _gateway.SendAsync(source.ToNumber, body, token);
            resend.RecordProviderResult(result.ProviderMessageSid, result.Status, result.ErrorCode, result.ErrorMessage);
        }
        catch (SmsGatewayException ex)
        {
            // Persist the outcome under the key so a repeat won't send again; the operator can retry with a fresh key.
            resend.RecordSendFailed(ex.Message);
            _logger.LogError("Operator re-send for notification {NotificationId} failed ({Kind}).", notificationId, ex.Kind);
        }

        await _notifications.AddAsync(resend, token);
        return ResendResult.Sent(resend);
    }

    public async Task<bool> RedactContentAsync(int notificationId, CancellationToken ct = default)
    {
        using var scope = BudgetScope(_settings.RequestBudgetSeconds, ct);
        var token = scope.Token;

        var notification = await _notifications.GetByIdAsync(notificationId, token);
        if (notification is null)
        {
            return false;
        }

        // Remove the text at the provider first — if that cannot be confirmed, surface it and keep the local
        // copy, so we never report the content as disposed of while it is still retrievable at the provider.
        if (notification.ProviderMessageSid is not null && !notification.ContentRedacted)
        {
            await _gateway.RedactBodyAsync(notification.ProviderMessageSid, token);
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, token);
        return true;
    }

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken ct = default)
    {
        using var scope = BudgetScope(_settings.RequestBudgetSeconds, ct);
        var token = scope.Token;

        var list = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(orderId), token);
        await RefreshPendingAsync(list, token);
        return list;
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<OrderNotification>>> GetOwnerNotificationsByOrderAsync(string ownerId, CancellationToken ct = default)
    {
        using var scope = BudgetScope(_settings.RequestBudgetSeconds, ct);
        var token = scope.Token;

        var list = await _notifications.ListAsync(new OrderNotificationsByOwnerSpecification(ownerId), token);
        await RefreshPendingAsync(list, token);

        return list
            .GroupBy(n => n.OrderId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<OrderNotification>)g.ToList());
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        using var scope = BudgetScope(_settings.ReconciliationBudgetSeconds, ct);
        var token = scope.Token;

        // Ask the provider for ITS record of messages from our own sending number over the range.
        var providerRecords = await _gateway.ListSentMessagesAsync(from, to, token);

        // Line them up against what eShop believes it sent, by message id.
        var eShopWithSid = await _notifications.ListAsync(new NotificationsWithProviderMessageSidSpecification(), token);
        var eShopBySid = eShopWithSid
            .Where(n => n.ProviderMessageSid is not null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var providerSids = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<ReconciliationEntry>();

        foreach (var record in providerRecords)
        {
            if (!string.IsNullOrEmpty(record.Sid))
            {
                providerSids.Add(record.Sid);
            }

            if (record.Sid is not null && eShopBySid.TryGetValue(record.Sid, out var known))
            {
                entries.Add(new ReconciliationEntry(record.Sid, ReconciliationOutcome.Matched,
                    record.Status, record.DateSent, known.Id, known.OrderId, known.Status));
            }
            else
            {
                entries.Add(new ReconciliationEntry(record.Sid, ReconciliationOutcome.ProviderOnly,
                    record.Status, record.DateSent, null, null, null));
            }
        }

        // eShop sent something in the window that the provider's ledger for the range does not list.
        foreach (var notification in eShopWithSid)
        {
            if (notification.ProviderMessageSid is null || providerSids.Contains(notification.ProviderMessageSid))
            {
                continue;
            }

            if (notification.CreatedAt < from || notification.CreatedAt > to)
            {
                continue;
            }

            entries.Add(new ReconciliationEntry(notification.ProviderMessageSid, ReconciliationOutcome.EShopOnly,
                null, null, notification.Id, notification.OrderId, notification.Status));
        }

        return new NotificationReconciliationReport(from, to, _settings.FromNumber, entries);
    }

    // --- helpers ------------------------------------------------------------------------------------

    private async Task SendAndRecordAsync(Order order, string toNumber, NotificationKind kind, string body, CancellationToken ct)
    {
        var notification = new OrderNotification(order.Id, order.BuyerId, toNumber, kind, body);
        try
        {
            var result = await _gateway.SendAsync(toNumber, body, ct);
            notification.RecordProviderResult(result.ProviderMessageSid, result.Status, result.ErrorCode, result.ErrorMessage);
        }
        catch (SmsGatewayException ex)
        {
            notification.RecordSendFailed(ex.Message);
            _logger.LogError("Failed to send {Kind} message for order {OrderId} ({ErrorKind}).", kind, order.Id, ex.Kind);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            notification.RecordSendFailed("Unexpected error sending the message.");
            _logger.LogError(ex, "Unexpected error sending {Kind} message for order {OrderId}.", kind, order.Id);
        }

        await _notifications.AddAsync(notification, ct);
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken ct)
    {
        var pending = await _notifications.ListAsync(new ScheduledFollowUpForOrderSpecification(orderId), ct);
        foreach (var followUp in pending)
        {
            if (followUp.ProviderMessageSid is null)
            {
                continue;
            }

            try
            {
                await _gateway.CancelScheduledAsync(followUp.ProviderMessageSid, ct);
                followUp.MarkCanceled();
                await _notifications.UpdateAsync(followUp, ct);
            }
            catch (SmsGatewayException ex)
            {
                // The order is still cancelled. Leave the record un-cancelled so it stays visible for follow-up.
                _logger.LogError("Could not cancel scheduled follow-up {NotificationId} for order {OrderId} ({Kind}).",
                    followUp.Id, orderId, ex.Kind);
            }
        }
    }

    private async Task RefreshPendingAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken ct)
    {
        var refreshed = 0;
        foreach (var notification in notifications)
        {
            if (!notification.IsPending() || refreshed >= MaxRefreshPerRequest)
            {
                continue;
            }

            if (ct.IsCancellationRequested)
            {
                break; // best-effort reporting — don't blow the request budget refreshing statuses
            }

            try
            {
                var state = await _gateway.FetchStatusAsync(notification.ProviderMessageSid!, ct);
                notification.RefreshStatus(state.Status, state.ErrorCode, state.ErrorMessage);
                await _notifications.UpdateAsync(notification, ct);
                refreshed++;
            }
            catch (SmsGatewayException ex)
            {
                _logger.LogWarning("Could not refresh status for notification {NotificationId} ({Kind}).", notification.Id, ex.Kind);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<string?> ResolveNumberAsync(string ownerId, CancellationToken ct)
    {
        // Send to the shopper's most recently registered number. Resolving live means a number the shopper
        // has since deleted is never used again.
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), ct);
        return numbers.FirstOrDefault()?.E164Number;
    }

    private static BudgetTokenScope BudgetScope(int budgetSeconds, CancellationToken ct)
        => new(budgetSeconds, ct);

    /// <summary>A linked cancellation scope giving a whole handler one deadline (its own calls' timeouts add up).</summary>
    private readonly struct BudgetTokenScope : IDisposable
    {
        private readonly CancellationTokenSource _cts;

        public BudgetTokenScope(int budgetSeconds, CancellationToken ct)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _cts.CancelAfter(TimeSpan.FromSeconds(budgetSeconds));
        }

        public CancellationToken Token => _cts.Token;

        public void Dispose() => _cts.Dispose();
    }
}
