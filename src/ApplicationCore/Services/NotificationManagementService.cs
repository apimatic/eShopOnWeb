using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class NotificationManagementService : INotificationManagementService
{
    private readonly IRepository<Notification> _notificationRepository;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<NotificationManagementService> _logger;

    public NotificationManagementService(IRepository<Notification> notificationRepository,
        ISmsGateway smsGateway,
        IAppLogger<NotificationManagementService> logger)
    {
        _notificationRepository = notificationRepository;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task<Notification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpecification(idempotencyKey), ct);
        if (existing != null)
        {
            if (existing.ResendOfNotificationId != notificationId)
            {
                throw new DuplicateException("The idempotency key was already used for a different resend.");
            }

            // Repeat under the same key: no second message; hand back what the first attempt produced.
            return existing;
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, ct);
        if (original == null)
        {
            throw new NotFoundException("Notification not found.");
        }

        if (original.ContentRedacted || original.Body == null)
        {
            throw new ConflictException("The message content has been disposed of and can no longer be sent.");
        }

        var resend = new Notification(original.OrderId, original.BuyerId, original.ToNumber,
            NotificationType.Resend, original.Body, idempotencyKey: idempotencyKey, resendOfNotificationId: notificationId);
        await _notificationRepository.AddAsync(resend, ct);

        try
        {
            var result = await _smsGateway.SendAsync(original.ToNumber, original.Body, ct);
            if (result.MessageSid != null)
            {
                resend.MarkSent(result.MessageSid, result.Status ?? "queued");
            }
            else
            {
                resend.MarkSendFailed(result.ErrorMessage ?? "The provider did not return a message identifier.");
            }
        }
        catch (Exception ex)
        {
            // The resend request itself still succeeds; the notification records the outcome.
            if (ex is SmsProviderException { OutcomeUnknown: true })
            {
                resend.MarkSendOutcomeUnknown("The send may have reached the provider before the connection failed.");
            }
            else
            {
                resend.MarkSendFailed("The message could not be sent.");
            }

            _logger.LogWarning("Failed to resend notification {NotificationId} (resend {ResendId}): {ExceptionType}.",
                notificationId, resend.Id, ex.GetType().Name);
        }

        await _notificationRepository.UpdateAsync(resend, ct);
        return resend;
    }

    public async Task DisposeContentAsync(int notificationId, CancellationToken ct = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, ct);
        if (notification == null)
        {
            throw new NotFoundException("Notification not found.");
        }

        if (notification.ContentRedacted)
        {
            return;
        }

        // Erase the text at the provider too — hiding it locally is not enough.
        if (notification.MessageSid != null)
        {
            await _smsGateway.RedactBodyAsync(notification.MessageSid, ct);
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, ct);
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var providerListing = await _smsGateway.ListSentAsync(from, to, ct);
        var providerMessages = providerListing.Messages;
        var localNotifications = await _notificationRepository.ListAsync(new NotificationsInRangeSpecification(from, to), ct);

        var localBySid = localNotifications
            .Where(n => n.MessageSid != null)
            .GroupBy(n => n.MessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var report = new NotificationReconciliationReport { From = from, To = to };
        var matchedSids = new HashSet<string>();

        foreach (var providerMessage in providerMessages)
        {
            if (providerMessage.MessageSid == null)
            {
                continue;
            }

            if (localBySid.TryGetValue(providerMessage.MessageSid, out var local))
            {
                matchedSids.Add(providerMessage.MessageSid);
                report.Entries.Add(new NotificationReconciliationEntry
                {
                    MessageSid = providerMessage.MessageSid,
                    NotificationId = local.Id,
                    Match = "Matched",
                    ProviderStatus = providerMessage.Status,
                    LocalStatus = local.Status,
                    DateSent = providerMessage.DateSent ?? providerMessage.DateCreated
                });
            }
            else
            {
                report.Entries.Add(new NotificationReconciliationEntry
                {
                    MessageSid = providerMessage.MessageSid,
                    Match = "ProviderOnly",
                    ProviderStatus = providerMessage.Status,
                    DateSent = providerMessage.DateSent ?? providerMessage.DateCreated
                });
            }
        }

        foreach (var local in localBySid)
        {
            if (matchedSids.Contains(local.Key))
            {
                continue;
            }

            report.Entries.Add(new NotificationReconciliationEntry
            {
                MessageSid = local.Key,
                NotificationId = local.Value.Id,
                Match = "LocalOnly",
                LocalStatus = local.Value.Status,
                DateSent = local.Value.CreatedAt
            });
        }

        report.MatchedCount = report.Entries.Count(e => e.Match == "Matched");
        report.ProviderOnlyCount = report.Entries.Count(e => e.Match == "ProviderOnly");
        report.LocalOnlyCount = report.Entries.Count(e => e.Match == "LocalOnly");
        report.ProviderListingTruncated = providerListing.Truncated;
        report.Entries = report.Entries.OrderBy(e => e.DateSent).ToList();
        return report;
    }
}
