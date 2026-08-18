using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class NotificationService : INotificationService
{
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ISmsSender _smsSender;
    private readonly IAppLogger<NotificationService> _logger;

    public NotificationService(
        IRepository<OrderNotification> notifications,
        ISmsSender smsSender,
        IAppLogger<NotificationService> logger)
    {
        _notifications = notifications;
        _smsSender = smsSender;
        _logger = logger;
    }

    public async Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        // A repeat under the same key must not send a second message: if a resend already ran for this
        // key, hand back the notification it produced without sending again.
        var priorForKey = await _notifications.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (priorForKey is not null)
        {
            _logger.LogInformation("Resend for idempotency key already handled; returning notification {NotificationId}.",
                priorForKey.Id);
            return priorForKey;
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            return null;
        }

        var body = NotificationMessageBuilder.Build(original.Kind, original.OrderId);
        var resent = new OrderNotification(original.OrderId, original.OwnerId, original.Kind, original.ToNumber,
            isScheduledFollowUp: false, idempotencyKey: idempotencyKey);
        try
        {
            var result = await _smsSender.SendAsync(original.ToNumber, body, cancellationToken);
            resent.RecordAccepted(result.MessageSid, result.Status);
        }
        catch (SmsProviderException ex)
        {
            // Record the attempt against the key (so a repeat still won't double-send) even if it failed.
            resent.RecordSendFailure();
            _logger.LogWarning("Resend of notification {NotificationId} failed at the provider (status {Status}).",
                notificationId, ex.StatusCode);
        }

        await _notifications.AddAsync(resent, cancellationToken);
        return resent;
    }

    public async Task<OrderNotification?> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return null;
        }

        // Redact the body at the provider so its text is no longer retrievable there. This must actually
        // happen — a provider failure is surfaced, not swallowed. The record and its status survive.
        if (notification.MessageSid is not null && !notification.ContentRedacted)
        {
            await _smsSender.RedactContentAsync(notification.MessageSid, cancellationToken);
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Disposed of content for notification {NotificationId}.", notificationId);
        return notification;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider only for messages sent from this application's own configured number.
        var providerMessages = await _smsSender.ListSentFromConfiguredNumberAsync(from, to, cancellationToken);

        // eShop's "believed sent" set over the same window.
        var eShopNotifications = await _notifications.ListAsync(
            new OrderNotificationsSentInRangeSpecification(from, to), cancellationToken);

        var providerBySid = providerMessages
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First());
        var eShopSids = eShopNotifications
            .Where(n => n.MessageSid is not null)
            .Select(n => n.MessageSid!)
            .ToHashSet();

        var matched = new List<ReconciliationMatch>();
        var eShopOnly = new List<OrderNotification>();
        foreach (var notification in eShopNotifications)
        {
            if (notification.MessageSid is not null && providerBySid.TryGetValue(notification.MessageSid, out var providerMessage))
            {
                matched.Add(new ReconciliationMatch(notification, providerMessage));
            }
            else
            {
                eShopOnly.Add(notification);
            }
        }

        var providerOnly = providerMessages
            .Where(m => !eShopSids.Contains(m.Sid))
            .ToList();

        _logger.LogInformation("Reconciliation over {From}..{To}: {Matched} matched, {ProviderOnly} provider-only, {EShopOnly} eShop-only.",
            from, to, matched.Count, providerOnly.Count, eShopOnly.Count);

        return new ReconciliationReport(from, to, matched, providerOnly, eShopOnly);
    }
}
