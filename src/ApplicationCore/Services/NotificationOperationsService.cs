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
using NotFoundException = Microsoft.eShopWeb.ApplicationCore.Exceptions.NotFoundException;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class NotificationOperationsService : INotificationOperationsService
{
    private readonly IRepository<Notification> _notifications;
    private readonly ISmsProvider _sms;
    private readonly IAppLogger<NotificationOperationsService> _logger;

    public NotificationOperationsService(
        IRepository<Notification> notifications,
        ISmsProvider sms,
        IAppLogger<NotificationOperationsService> logger)
    {
        _notifications = notifications;
        _sms = sms;
        _logger = logger;
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotFoundException($"Notification {notificationId} was not found.");

        // Repeating a resend under the same idempotency key must not send a second message.
        var priorAttempt = await _notifications.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (priorAttempt is not null)
        {
            _logger.LogInformation("Resend under idempotency key reused prior notification {NotificationId}.", priorAttempt.Id);
            return new ResendResult(priorAttempt, Reused: true);
        }

        // Reproduce the message. If the original content was disposed of, regenerate wording from its kind.
        var body = original.Body ?? NotificationMessages.ForKind(original.Kind, original.OrderId);
        var resend = Notification.Create(original.OwnerId, original.OrderId, original.Kind, original.ToNumber, body);
        resend.AssignIdempotencyKey(idempotencyKey);

        try
        {
            var result = await _sms.SendAsync(original.ToNumber, body, cancellationToken);
            if (result.Accepted && result.Sid is not null)
            {
                resend.RecordAccepted(result.Sid, result.Status ?? "queued");
            }
            else
            {
                resend.RecordSendFailure(result.ErrorCode, result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to resend message for notification {NotificationId}: {Error}", notificationId, ex.Message);
            resend.RecordSendFailure(null, "The message could not be sent.");
        }

        await _notifications.AddAsync(resend, cancellationToken);
        _logger.LogInformation("Resend of notification {OriginalId} produced notification {NotificationId}.", notificationId, resend.Id);
        return new ResendResult(resend, Reused: false);
    }

    public async Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotFoundException($"Notification {notificationId} was not found.");

        // The content must no longer be retrievable from the provider either, not merely hidden here.
        if (notification.ProviderMessageSid is not null)
        {
            var redacted = await _sms.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
            if (!redacted)
            {
                throw new InvalidOperationException(
                    $"The provider did not dispose of the content for notification {notificationId}; content was not disposed.");
            }
        }

        notification.DisposeContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Disposed of content for notification {NotificationId}.", notificationId);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var fromNumber = _sms.SendingNumber;

        // Ask the provider for this sending number's messages in the range (server-side filter), covering the whole range.
        var providerMessages = await _sms.ListSentFromAsync(fromNumber, from, to, cancellationToken);
        var providerInRange = providerMessages
            .Where(m => m.DateSent is null || (m.DateSent >= from && m.DateSent <= to))
            .ToList();

        var ourNotifications = await _notifications.ListAsync(
            new NotificationsWithProviderIdInRangeSpecification(from, to), cancellationToken);
        var ourBySid = ourNotifications
            .Where(n => n.ProviderMessageSid is not null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());
        var providerSids = providerInRange.Select(m => m.Sid).ToHashSet();

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        foreach (var message in providerInRange)
        {
            if (ourBySid.TryGetValue(message.Sid, out var notification))
            {
                matched.Add(new ReconciliationEntry(message.Sid, message.Status, notification.Id, message.DateSent));
            }
            else
            {
                // The provider knows about it, eShop does not.
                providerOnly.Add(new ReconciliationEntry(message.Sid, message.Status, null, message.DateSent));
            }
        }

        // eShop believes it sent these, but the provider's record for the range does not include them.
        var eShopOnly = ourBySid.Values
            .Where(n => !providerSids.Contains(n.ProviderMessageSid!))
            .Select(n => new ReconciliationEntry(n.ProviderMessageSid!, n.DeliveryStatus, n.Id, null))
            .ToList();

        return new ReconciliationReport(
            from, to, fromNumber,
            ProviderCount: providerInRange.Count,
            EShopCount: ourBySid.Count,
            Matched: matched,
            ProviderOnly: providerOnly,
            EShopOnly: eShopOnly);
    }
}
