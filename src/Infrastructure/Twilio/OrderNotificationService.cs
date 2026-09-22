using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Orchestrates order notifications over the provider-neutral <see cref="ISmsGateway"/>. Each send writes a
/// local <see cref="OrderNotification"/> row before calling the provider and completes it after, so a
/// provider-side message always has a local row to reconcile against. Sends are best-effort: a provider
/// failure is recorded, never propagated to fail the order operation. The shopper's number and message
/// text are never logged.
/// </summary>
public sealed class OrderNotificationService : IOrderNotificationService
{
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IReadRepository<ContactNumber> _contactNumbers;
    private readonly ISmsGateway _gateway;
    private readonly TwilioSettings _settings;
    private readonly ILogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notifications,
        IReadRepository<ContactNumber> contactNumbers,
        ISmsGateway gateway,
        IOptions<TwilioSettings> settings,
        ILogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _gateway = gateway;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task SendOrderPlacedAsync(Order order, CancellationToken ct)
    {
        foreach (var number in await NumbersFor(order.BuyerId, ct))
        {
            await SendNowAsync(order, number, NotificationKind.OrderPlaced,
                $"eShop: your order #{order.Id} has been placed. Thank you!", ct);
        }
    }

    public async Task SendDispatchedAsync(Order order, CancellationToken ct)
    {
        var sendAt = DateTimeOffset.UtcNow.AddDays(Math.Max(1, _settings.FeedbackFollowUpDelayDays));

        foreach (var number in await NumbersFor(order.BuyerId, ct))
        {
            await SendNowAsync(order, number, NotificationKind.OrderDispatched,
                $"eShop: your order #{order.Id} is on its way!", ct);

            // Queue the "how did the delivery go?" follow-up WITH THE PROVIDER for a few days later.
            await ScheduleFollowUpAsync(order, number, sendAt,
                $"eShop: how did the delivery of your order #{order.Id} go? Reply to let us know.", ct);
        }
    }

    public async Task SendCancelledAsync(Order order, CancellationToken ct)
    {
        foreach (var number in await NumbersFor(order.BuyerId, ct))
        {
            await SendNowAsync(order, number, NotificationKind.OrderCancelled,
                $"eShop: your order #{order.Id} has been cancelled.", ct);
        }

        // Independently of current numbers: call off every follow-up that has not yet gone out, so asking
        // "how did the delivery go" for a cancelled order can never happen.
        var pending = await _notifications.ListAsync(
            new PendingFeedbackNotificationsByOrderSpecification(order.Id), ct);

        foreach (var followUp in pending)
        {
            try
            {
                await _gateway.CancelScheduledAsync(followUp.MessageSid!, ct);
                followUp.MarkScheduledCanceled();
                await _notifications.UpdateAsync(followUp, ct);
            }
            catch (TwilioProviderException ex)
            {
                _logger.LogWarning(ex,
                    "Could not cancel scheduled follow-up notification {NotificationId} for order {OrderId} (status {Status})",
                    followUp.Id, order.Id, (int?)ex.StatusCode);
            }
        }
    }

    public async Task<ResendOutcome> ResendAsync(OrderNotification original, string idempotencyKey,
        CancellationToken ct)
    {
        // Non-authoritative fast path: a prior resend under this key is returned as-is (also covers the
        // in-memory provider, which does not enforce the unique index).
        var existing = await _notifications.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), ct);
        if (existing != null)
        {
            return new ResendOutcome(existing.Id, true);
        }

        var body = original.Body ?? $"eShop: an update about your order #{original.OrderId}.";

        var resend = new OrderNotification(
            orderId: original.OrderId,
            buyerId: original.BuyerId,
            kind: original.Kind,
            toNumber: original.ToNumber,
            fromAddress: _settings.FromNumber,
            body: body,
            idempotencyKey: idempotencyKey,
            resendOfNotificationId: original.Id);

        // The claim: the row (with its unique idempotency key) is written BEFORE the provider call.
        try
        {
            await _notifications.AddAsync(resend, ct);
        }
        catch (DbUpdateException)
        {
            // The unique index rejected a concurrent second insert under the same key — return the winner.
            var winner = await _notifications.FirstOrDefaultAsync(
                new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), ct);
            if (winner != null)
            {
                return new ResendOutcome(winner.Id, true);
            }

            throw;
        }

        try
        {
            var result = await _gateway.SendAsync(original.ToNumber, body, ct);
            resend.RecordAccepted(result.MessageSid, result.Status, MapStatus(result.Status),
                result.ErrorCode, result.DateSent);
        }
        catch (TwilioProviderException ex)
        {
            _logger.LogWarning(ex, "Resend of notification {NotificationId} failed at the provider (status {Status})",
                original.Id, (int?)ex.StatusCode);
            resend.RecordSendFailed();
        }

        await _notifications.UpdateAsync(resend, ct);
        return new ResendOutcome(resend.Id, false);
    }

    public async Task RedactContentAsync(OrderNotification notification, CancellationToken ct)
    {
        // Redact at the provider first (may throw — this is an operator action, so surface a failure),
        // then dispose of the local copy. The record of the send and its outcome survives.
        if (!string.IsNullOrEmpty(notification.MessageSid))
        {
            await _gateway.RedactAsync(notification.MessageSid!, ct);
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, ct);
    }

    public async Task RefreshOutcomesAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken ct)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.MessageSid) || IsTerminal(notification.DeliveryState))
            {
                continue;
            }

            try
            {
                var status = await _gateway.FetchAsync(notification.MessageSid!, ct);
                notification.RefreshFromProvider(status.Status, MapStatus(status.Status), status.ErrorCode,
                    status.DateSent);
                await _notifications.UpdateAsync(notification, ct);
            }
            catch (TwilioProviderException ex)
            {
                _logger.LogDebug(ex, "Could not refresh notification {NotificationId} from the provider",
                    notification.Id);
            }
        }
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct)
    {
        var provider = await _gateway.ListSentFromConfiguredSenderAsync(from, to, ct);

        // Local side: messages we sent from the configured sender. Both sides are compared on the provider's
        // own send-time clock (DateSent), never on a local row-creation column.
        var local = await _notifications.ListAsync(
            new NotificationsSentFromSenderSpecification(_settings.FromNumber), ct);

        var providerBySid = new Dictionary<string, ProviderMessage>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in provider.Messages)
        {
            providerBySid[m.Sid] = m;
        }

        var localSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matched = new List<ReconciliationMatch>();
        var eShopOnly = new List<ReconciliationEShopOnly>();
        var providerOnly = new List<ReconciliationProviderOnly>();

        var localInWindow = 0;
        foreach (var n in local)
        {
            var sid = n.MessageSid!;
            localSids.Add(sid);

            // Only rows whose provider send-time falls in the window are comparable to the provider's list.
            if (!InWindow(n.ProviderDateSent, from, to))
            {
                continue;
            }

            localInWindow++;

            if (providerBySid.TryGetValue(sid, out var pm))
            {
                matched.Add(new ReconciliationMatch(sid, n.Id, n.OrderId, n.Kind,
                    pm.Status ?? n.ProviderStatus, n.DeliveryState.ToString(), pm.DateSent ?? n.ProviderDateSent));
            }
            else
            {
                eShopOnly.Add(new ReconciliationEShopOnly(n.Id, n.OrderId, sid, n.Kind, n.DeliveryState.ToString()));
            }
        }

        foreach (var m in provider.Messages)
        {
            if (!localSids.Contains(m.Sid))
            {
                providerOnly.Add(new ReconciliationProviderOnly(m.Sid, m.Status, m.To, m.DateSent));
            }
        }

        return new ReconciliationReport
        {
            From = from,
            To = to,
            ProviderResultsTruncated = provider.Truncated,
            ProviderCount = provider.Messages.Count,
            EShopCount = localInWindow,
            Matched = matched,
            ProviderOnly = providerOnly,
            EShopOnly = eShopOnly
        };
    }

    private async Task<IReadOnlyList<ContactNumber>> NumbersFor(string buyerId, CancellationToken ct) =>
        await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);

    private async Task SendNowAsync(Order order, ContactNumber number, NotificationKind kind, string body,
        CancellationToken ct)
    {
        var notification = new OrderNotification(order.Id, order.BuyerId, kind, number.PhoneNumber,
            _settings.FromNumber, body);
        await _notifications.AddAsync(notification, ct); // local record BEFORE the provider call

        try
        {
            var result = await _gateway.SendAsync(number.PhoneNumber, body, ct);
            notification.RecordAccepted(result.MessageSid, result.Status, MapStatus(result.Status),
                result.ErrorCode, result.DateSent);
        }
        catch (TwilioProviderException ex)
        {
            _logger.LogWarning(ex, "Notification {Kind} for order {OrderId} failed to send (status {Status})",
                kind, order.Id, (int?)ex.StatusCode);
            notification.RecordSendFailed();
        }

        await _notifications.UpdateAsync(notification, ct);
    }

    private async Task ScheduleFollowUpAsync(Order order, ContactNumber number, DateTimeOffset sendAt,
        string body, CancellationToken ct)
    {
        // FromAddress is null: a scheduled message goes via the messaging service, whose sender is only
        // chosen when it actually sends — so it stays out of the FromNumber reconciliation set.
        var notification = new OrderNotification(order.Id, order.BuyerId, NotificationKind.DeliveryFeedback,
            number.PhoneNumber, fromAddress: null, body: body, scheduledSendAt: sendAt);
        await _notifications.AddAsync(notification, ct);

        try
        {
            var result = await _gateway.ScheduleAsync(number.PhoneNumber, body, sendAt, ct);
            notification.RecordAccepted(result.MessageSid, result.Status, MapStatus(result.Status),
                result.ErrorCode, result.DateSent);
        }
        catch (TwilioProviderException ex)
        {
            _logger.LogWarning(ex, "Follow-up for order {OrderId} could not be scheduled (status {Status})",
                order.Id, (int?)ex.StatusCode);
            notification.RecordSendFailed();
        }

        await _notifications.UpdateAsync(notification, ct);
    }

    private static bool IsTerminal(NotificationDeliveryState state) =>
        state is NotificationDeliveryState.Delivered
            or NotificationDeliveryState.Failed
            or NotificationDeliveryState.Undelivered
            or NotificationDeliveryState.Canceled
            or NotificationDeliveryState.SendFailed;

    private static bool InWindow(string? providerDateSent, DateTimeOffset from, DateTimeOffset to)
    {
        if (string.IsNullOrEmpty(providerDateSent))
        {
            return false; // no provider send-time yet (e.g. still scheduled) → out of window, not a discrepancy
        }

        return DateTimeOffset.TryParse(providerDateSent, out var sent) && sent >= from && sent <= to;
    }

    /// <summary>Map the provider's raw status to eShop's outcome. An absent/unknown status is Pending — never success.</summary>
    private static NotificationDeliveryState MapStatus(string? providerStatus) => providerStatus switch
    {
        "delivered" => NotificationDeliveryState.Delivered,
        "sent" => NotificationDeliveryState.Sent,
        "undelivered" => NotificationDeliveryState.Undelivered,
        "failed" => NotificationDeliveryState.Failed,
        "canceled" => NotificationDeliveryState.Canceled,
        "scheduled" => NotificationDeliveryState.Scheduled,
        "queued" or "sending" or "accepted" or "receiving" or "received" => NotificationDeliveryState.Queued,
        _ => NotificationDeliveryState.Pending
    };
}
