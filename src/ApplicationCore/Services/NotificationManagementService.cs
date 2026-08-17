using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class NotificationManagementService : INotificationManagementService
{
    private readonly IRepository<SmsNotification> _notifications;
    private readonly IReadRepository<Order> _orders;
    private readonly INotificationDispatcher _dispatcher;
    private readonly ISmsProvider _provider;
    private readonly IAppLogger<NotificationManagementService> _logger;

    public NotificationManagementService(
        IRepository<SmsNotification> notifications,
        IReadRepository<Order> orders,
        INotificationDispatcher dispatcher,
        ISmsProvider provider,
        IAppLogger<NotificationManagementService> logger)
    {
        _notifications = notifications;
        _orders = orders;
        _dispatcher = dispatcher;
        _provider = provider;
        _logger = logger;
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // A repeat under the same key returns the message the first request produced, sending nothing.
        var priorForKey = await _notifications.FirstOrDefaultAsync(
            new SmsNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (priorForKey is not null)
        {
            _logger.LogInformation($"Resend under an existing idempotency key replayed message id {priorForKey.Id}.");
            return new ResendResult(ResendOutcome.ReplayedIdempotent, priorForKey);
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            return new ResendResult(ResendOutcome.NotFound, null);
        }

        // Do not resurrect disposed content: if the original body was redacted, recompose a fresh message.
        var body = original.Body;
        if (string.IsNullOrEmpty(body))
        {
            var order = await _orders.GetByIdAsync(original.OrderId, cancellationToken);
            body = order is not null
                ? NotificationMessages.For(original.Kind, order)
                : $"eShop: an update on your order #{original.OrderId}.";
        }

        var resend = new SmsNotification(
            original.OrderId,
            original.BuyerId,
            original.ToNumber,
            body!,
            original.Kind,
            scheduledFor: null,
            idempotencyKey: idempotencyKey,
            resentFromNotificationId: original.Id);

        await _dispatcher.SendNewAsync(resend, cancellationToken);
        _logger.LogInformation($"Resent message for order {original.OrderId} as new message id {resend.Id}.");
        return new ResendResult(ResendOutcome.Created, resend);
    }

    public async Task<bool> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return false;
        }

        // Dispose of the text at the provider first, so it is no longer retrievable there, then locally.
        if (!string.IsNullOrEmpty(notification.ProviderMessageId))
        {
            await _provider.RedactBodyAsync(notification.ProviderMessageId, cancellationToken);
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation($"Disposed of content for notification id {notificationId}.");
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var fromNumber = _provider.ConfiguredSenderNumber;

        // Ask the provider only for this number's messages, then trim to the precise instant range.
        var providerStates = await _provider.ListSentFromAsync(fromNumber, from, to, cancellationToken);
        var providerInRange = providerStates
            .Where(m => m.DateSent.HasValue && m.DateSent.Value >= from && m.DateSent.Value <= to)
            .ToList();

        var eShopNotifications = await _notifications.ListAsync(new SmsNotificationsSentBetweenSpecification(from, to), cancellationToken);

        var providerBySid = providerInRange
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First());
        var eShopBySid = eShopNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageId))
            .GroupBy(n => n.ProviderMessageId!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var eShopOnly = new List<ReconciliationEntry>();

        foreach (var (sid, state) in providerBySid)
        {
            if (eShopBySid.TryGetValue(sid, out var known))
            {
                matched.Add(new ReconciliationEntry(
                    sid, PhoneMask.Mask(state.To), state.Status, state.ErrorCode, state.DateSent,
                    known.Id, known.Status.ToString()));
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry(
                    sid, PhoneMask.Mask(state.To), state.Status, state.ErrorCode, state.DateSent, null, null));
            }
        }

        foreach (var notification in eShopNotifications)
        {
            var sid = notification.ProviderMessageId;
            if (string.IsNullOrEmpty(sid) || !providerBySid.ContainsKey(sid))
            {
                eShopOnly.Add(new ReconciliationEntry(
                    sid ?? string.Empty, PhoneMask.Mask(notification.ToNumber), notification.ProviderStatus,
                    notification.ErrorCode, notification.SentAt, notification.Id, notification.Status.ToString()));
            }
        }

        _logger.LogInformation(
            $"Reconciliation {from:o}..{to:o}: provider {providerInRange.Count}, eShop {eShopNotifications.Count}, matched {matched.Count}, provider-only {providerOnly.Count}, eShop-only {eShopOnly.Count}.");

        return new ReconciliationReport(
            from, to, fromNumber, providerInRange.Count, eShopNotifications.Count, matched, providerOnly, eShopOnly);
    }
}
