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

/// <summary>Operator actions over notifications: resend, content disposal and reconciliation.</summary>
public class NotificationOperationsService : INotificationOperationsService
{
    private readonly IRepository<Notification> _notifications;
    private readonly ITwilioMessagingClient _messaging;
    private readonly ISmsConfiguration _configuration;
    private readonly IAppLogger<NotificationOperationsService> _logger;

    public NotificationOperationsService(
        IRepository<Notification> notifications,
        ITwilioMessagingClient messaging,
        ISmsConfiguration configuration,
        IAppLogger<NotificationOperationsService> logger)
    {
        _notifications = notifications;
        _messaging = messaging;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<Notification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        // Idempotency: a repeat under the same key returns the message the first request
        // produced rather than sending another.
        var alreadyDone = await _notifications.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (alreadyDone is not null)
        {
            _logger.LogInformation("Resend under an existing idempotency key returned notification {NotificationId} without re-sending.", alreadyDone.Id);
            return alreadyDone;
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
            return null;

        if (string.IsNullOrEmpty(original.Body))
            throw new InvalidRequestException("The message content has been disposed of and cannot be resent.");

        var resend = new Notification(
            original.OrderId, original.BuyerId, original.Kind, original.ToPhoneNumber, original.Body,
            idempotencyKey: idempotencyKey, resendOfNotificationId: original.Id);

        // Persist the key before sending so a concurrent/retried request cannot send twice.
        await _notifications.AddAsync(resend, cancellationToken);

        try
        {
            var message = await _messaging.SendMessageAsync(new SendMessageCommand
            {
                To = original.ToPhoneNumber,
                Body = original.Body!,
                From = _configuration.SenderNumber
            }, cancellationToken);
            resend.RecordProviderResult(message.Sid, message.Status, message.ErrorCode, message.ErrorMessage);
        }
        catch (TwilioApiException ex)
        {
            resend.RecordSendFailure(ex.TwilioCode is int code ? $"provider rejected the request (code {code})" : $"provider returned HTTP {(int)ex.StatusCode}");
            _logger.LogWarning("Resend as notification {NotificationId} failed at the provider.", resend.Id);
        }
        catch (Exception)
        {
            resend.RecordSendFailure("the provider could not be reached");
            _logger.LogWarning("Resend as notification {NotificationId} could not reach the provider.", resend.Id);
        }

        await _notifications.UpdateAsync(resend, cancellationToken);
        return resend;
    }

    public async Task<bool> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
            return false;

        // Dispose of the content at the provider so its text is no longer retrievable there.
        // If the message never reached the provider (no SID), there is nothing to redact remotely.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            var message = await _messaging.RedactMessageBodyAsync(notification.ProviderMessageSid!, cancellationToken);
            // The provider keeps the record and its outcome; only the body is emptied.
            notification.UpdateStatus(message.Status, message.ErrorCode, message.ErrorMessage);
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Disposed of the content of notification {NotificationId}.", notificationId);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var fromNumber = _configuration.SenderNumber;

        // Ask the provider specifically for this application's own sending number's messages,
        // rather than filtering a wider answer after the fact.
        var providerMessages = await _messaging.ListMessagesAsync(new TwilioMessageListQuery
        {
            From = fromNumber,
            DateSentAfter = from,
            DateSentBefore = to,
            PageSize = 1000
        }, cancellationToken);

        // Narrow the provider's day-granular result to messages actually sent within the range.
        var provider = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid) && m.DateSent.HasValue && m.DateSent.Value >= from && m.DateSent.Value <= to)
            .GroupBy(m => m.Sid!)
            .ToDictionary(g => g.Key, g => g.First());

        // eShop's side: notifications it believes it actually sent (have a provider SID, are not
        // still merely scheduled/pending) within the range.
        var allNotifications = await _notifications.ListAsync(cancellationToken);
        var eShop = allNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid)
                        && n.Status != NotificationStatus.Scheduled
                        && n.Status != NotificationStatus.Pending
                        && n.CreatedDate >= from && n.CreatedDate <= to)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationMatch>();
        var onlyAtProvider = new List<ReconciliationEntry>();
        var onlyInEShop = new List<ReconciliationEntry>();

        foreach (var (sid, message) in provider)
        {
            if (eShop.TryGetValue(sid, out var notification))
                matched.Add(new ReconciliationMatch(sid, message.Status, notification.Status, notification.Id));
            else
                onlyAtProvider.Add(new ReconciliationEntry(sid, message.Status, null));
        }

        foreach (var (sid, notification) in eShop)
        {
            if (!provider.ContainsKey(sid))
                onlyInEShop.Add(new ReconciliationEntry(sid, notification.Status, notification.Id));
        }

        _logger.LogInformation(
            "Reconciliation {From:o}..{To:o}: provider {ProviderCount}, eShop {EShopCount}, matched {Matched}, only-provider {OnlyProvider}, only-eShop {OnlyEShop}.",
            from, to, provider.Count, eShop.Count, matched.Count, onlyAtProvider.Count, onlyInEShop.Count);

        return new ReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = fromNumber,
            ProviderMessageCount = provider.Count,
            EShopMessageCount = eShop.Count,
            MatchedCount = matched.Count,
            OnlyAtProvider = onlyAtProvider,
            OnlyInEShop = onlyInEShop,
            Matched = matched
        };
    }
}
