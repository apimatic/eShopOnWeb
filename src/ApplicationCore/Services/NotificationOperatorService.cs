using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class NotificationOperatorService : INotificationOperatorService
{
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ITwilioMessageClient _twilio;
    private readonly OrderNotificationSender _sender;
    private readonly IAppLogger<NotificationOperatorService> _logger;

    public NotificationOperatorService(
        IRepository<OrderNotification> notifications,
        ITwilioMessageClient twilio,
        OrderNotificationSender sender,
        IAppLogger<NotificationOperatorService> logger)
    {
        _notifications = notifications;
        _twilio = twilio;
        _sender = sender;
        _logger = logger;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var existing = await _notifications.FirstOrDefaultAsync(
            new ResendByIdempotencyKeySpec(notificationId, idempotencyKey),
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            throw new KeyNotFoundException("Notification was not found.");
        }

        try
        {
            var resent = await _sender.TrySendResendAsync(original, idempotencyKey, cancellationToken);
            if (resent is null)
            {
                throw new NotificationException("The destination is no longer on file; the message was not re-sent.");
            }

            return resent;
        }
        catch (InvalidOperationException ex)
        {
            throw new NotificationException(ex.Message);
        }
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            throw new KeyNotFoundException("Notification was not found.");
        }

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            var redacted = await _twilio.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
            notification.ApplyProviderStatus(redacted.Status, redacted.ErrorCode, redacted.ErrorMessage);
        }

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The 'to' timestamp must be on or after 'from'.");
        }

        var fromNumber = _twilio.FromNumber;
        if (string.IsNullOrWhiteSpace(fromNumber))
        {
            throw new InvalidOperationException("Twilio:FromNumber is not configured.");
        }

        var providerMessages = await _twilio.ListSentFromAsync(fromNumber, from, to, cancellationToken);
        var eShopRecords = await _notifications.ListAsync(
            new NotificationsWithProviderSidInRangeSpec(from, to),
            cancellationToken);

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var eShopBySid = eShopRecords
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var extraProviderSids = providerBySid.Keys.Except(eShopBySid.Keys, StringComparer.Ordinal).ToArray();
        if (extraProviderSids.Length > 0)
        {
            var extraLocal = await _notifications.ListAsync(
                new NotificationsByProviderSidsSpec(extraProviderSids),
                cancellationToken);
            foreach (var local in extraLocal)
            {
                if (!string.IsNullOrEmpty(local.ProviderMessageSid))
                {
                    eShopBySid[local.ProviderMessageSid] = local;
                }
            }
        }

        var matched = new List<ReconciledMessage>();
        var providerOnly = new List<ReconciledMessage>();
        var eShopOnly = new List<ReconciledMessage>();

        foreach (var (sid, provider) in providerBySid)
        {
            if (eShopBySid.TryGetValue(sid, out var local))
            {
                matched.Add(ToReconciled(local, provider));
            }
            else
            {
                providerOnly.Add(new ReconciledMessage
                {
                    ProviderMessageSid = sid,
                    ProviderStatus = provider.Status,
                    DateSent = provider.DateSent,
                    DateCreated = provider.DateCreated
                });
            }
        }

        foreach (var (sid, local) in eShopBySid)
        {
            if (!providerBySid.ContainsKey(sid))
            {
                eShopOnly.Add(ToReconciled(local, null));
            }
        }

        _logger.LogInformation(
            "Reconciliation from {From} to {To} matched {Matched}, provider-only {ProviderOnly}, eShop-only {EShopOnly}.",
            from, to, matched.Count, providerOnly.Count, eShopOnly.Count);

        return new NotificationReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = fromNumber,
            Matched = matched,
            ProviderOnly = providerOnly,
            EShopOnly = eShopOnly
        };
    }

    private static ReconciledMessage ToReconciled(OrderNotification local, TwilioMessageSnapshot? provider)
    {
        return new ReconciledMessage
        {
            NotificationId = local.Id,
            ProviderMessageSid = local.ProviderMessageSid,
            EShopStatus = local.ProviderStatus,
            ProviderStatus = provider?.Status,
            Kind = local.Kind,
            DateSent = provider?.DateSent,
            DateCreated = provider?.DateCreated
        };
    }
}
