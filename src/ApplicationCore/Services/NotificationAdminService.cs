using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class NotificationAdminService : INotificationAdminService
{
    private readonly IRepository<Notification> _notificationRepository;
    private readonly ISmsGateway _smsGateway;
    private readonly ISmsSenderIdentity _senderIdentity;
    private readonly IAppLogger<NotificationAdminService> _logger;

    public NotificationAdminService(
        IRepository<Notification> notificationRepository,
        ISmsGateway smsGateway,
        ISmsSenderIdentity senderIdentity,
        IAppLogger<NotificationAdminService> logger)
    {
        _notificationRepository = notificationRepository;
        _smsGateway = smsGateway;
        _senderIdentity = senderIdentity;
        _logger = logger;
    }

    public async Task<ResendResult?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var original = await _notificationRepository.FirstOrDefaultAsync(
            new NotificationByIdSpecification(notificationId), cancellationToken);
        if (original is null) return null;

        // Idempotency: a repeat under the same key returns the message the first request produced,
        // and never sends a second. A genuine second attempt under a fresh key sends again.
        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (existing is not null)
        {
            return new ResendResult(existing, WasReplay: true);
        }

        // The content may have been disposed of; fall back to a neutral re-send body in that case.
        var body = string.IsNullOrEmpty(original.Body)
            ? $"A message about your eShop order #{original.OrderId} is being re-sent."
            : original.Body;

        var resend = new Notification(original.OrderId, original.OwnerId, original.ToNumber,
            original.Type, body, idempotencyKey: idempotencyKey);
        await _notificationRepository.AddAsync(resend, cancellationToken);

        try
        {
            var result = await _smsGateway.SendAsync(new SendSmsRequest(resend.ToNumber, body), cancellationToken);
            resend.RecordAccepted(result.Sid, result.Status, result.ErrorCode);
        }
        catch (Exception ex)
        {
            resend.RecordSendFailure();
            _logger.LogWarning("Resend failed for notification {0} (order {1}): {2}",
                resend.Id, resend.OrderId, ex.Message);
        }

        await _notificationRepository.UpdateAsync(resend, cancellationToken);
        return new ResendResult(resend, WasReplay: false);
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.FirstOrDefaultAsync(
            new NotificationByIdSpecification(notificationId), cancellationToken);
        if (notification is null) return false;

        // Remove the text at the provider so it is no longer retrievable there either. The record that
        // a message was sent, and what became of it, survives.
        if (!string.IsNullOrEmpty(notification.ProviderMessageId))
        {
            await _smsGateway.RedactBodyAsync(notification.ProviderMessageId, cancellationToken);
        }

        notification.DisposeContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var fromNumber = _senderIdentity.FromNumber;

        // Ask the provider for THIS sending number's messages over the range, rather than filtering a
        // wider answer after the fact.
        var providerMessages = await _smsGateway.ListSentFromAsync(fromNumber, from, to, cancellationToken);

        // What this app believes it sent in the same window: notifications that carry a provider id.
        var eShopSent = await _notificationRepository.ListAsync(
            new SentNotificationsBetweenSpecification(from, to), cancellationToken);

        var eShopBySid = eShopSent
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageId))
            .GroupBy(n => n.ProviderMessageId!)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var providerBySid = providerMessages
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var eShopOnly = new List<ReconciliationEntry>();

        foreach (var message in providerBySid.Values)
        {
            if (eShopBySid.TryGetValue(message.Sid, out var notification))
            {
                matched.Add(new ReconciliationEntry(message.Sid, message.Status,
                    notification.Id, notification.OrderId, message.DateSent));
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry(message.Sid, message.Status, null, null, message.DateSent));
            }
        }

        foreach (var notification in eShopBySid.Values)
        {
            if (!providerBySid.ContainsKey(notification.ProviderMessageId!))
            {
                eShopOnly.Add(new ReconciliationEntry(notification.ProviderMessageId!, notification.Status,
                    notification.Id, notification.OrderId, null));
            }
        }

        return new ReconciliationReport(from, to, fromNumber,
            ProviderCount: providerBySid.Count,
            EShopCount: eShopBySid.Count,
            Matched: matched,
            ProviderOnly: providerOnly,
            EShopOnly: eShopOnly);
    }
}
