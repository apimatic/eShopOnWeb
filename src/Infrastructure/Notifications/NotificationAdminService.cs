using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

/// <summary>Flow 3 implementation — resend, content disposal, reconciliation.</summary>
public class NotificationAdminService : INotificationAdminService
{
    private const int ReconciliationPageSize = 1000;
    private const int MaxReconciliationPages = 200; // safety backstop; surfaced as Truncated if hit

    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<NotificationIdempotencyRecord> _idempotencyRepository;
    private readonly ISmsProviderGateway _gateway;
    private readonly TwilioSettings _settings;
    private readonly ILogger<NotificationAdminService> _logger;

    public NotificationAdminService(
        IRepository<OrderNotification> notificationRepository,
        IRepository<NotificationIdempotencyRecord> idempotencyRepository,
        ISmsProviderGateway gateway,
        IOptions<TwilioSettings> settings,
        ILogger<NotificationAdminService> logger)
    {
        _notificationRepository = notificationRepository;
        _idempotencyRepository = idempotencyRepository;
        _gateway = gateway;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct)
    {
        // Fast path / in-memory-provider correctness: a prior claim under this key returns the prior result.
        var priorClaim = await _idempotencyRepository.FirstOrDefaultAsync(
            new NotificationIdempotencyByKeySpecification(idempotencyKey), ct);
        if (priorClaim is not null)
        {
            return new ResendResult(ResendOutcome.DuplicateIgnored, priorClaim.ResultNotificationId,
                "A resend under this key has already been made.");
        }

        var source = await _notificationRepository.GetByIdAsync(notificationId, ct);
        if (source is null)
        {
            return new ResendResult(ResendOutcome.NotFound, null, "Notification not found.");
        }

        if (source.ContentRedacted || string.IsNullOrEmpty(source.Content) || string.IsNullOrEmpty(source.ToNumber))
        {
            return new ResendResult(ResendOutcome.NothingToResend, null,
                "This notification has no content or destination to resend.");
        }

        // WRITE ORDER: create the local record for the resend, then claim the key, then call the provider.
        var resend = new OrderNotification(source.OrderId, source.OwnerId, NotificationKind.Resend,
            source.ToNumber, source.Content, resendOfNotificationId: source.Id);
        await _notificationRepository.AddAsync(resend, ct);

        try
        {
            var claim = new NotificationIdempotencyRecord(idempotencyKey, source.Id, resend.Id);
            await _idempotencyRepository.AddAsync(claim, ct);
        }
        catch (DbUpdateException)
        {
            // A concurrent request claimed the key first: the unique constraint rejected this row. Do not send;
            // discard the just-created resend row and return the winner's result.
            await _notificationRepository.DeleteAsync(resend, ct);
            var winner = await _idempotencyRepository.FirstOrDefaultAsync(
                new NotificationIdempotencyByKeySpecification(idempotencyKey), ct);
            return new ResendResult(ResendOutcome.DuplicateIgnored, winner?.ResultNotificationId,
                "A resend under this key has already been made.");
        }

        try
        {
            var result = await _gateway.SendAsync(source.ToNumber, source.Content, ct);
            resend.RecordSent(result.ProviderMessageSid,
                NotificationStatusMapper.FromProviderStatus(result.ProviderStatusRaw),
                result.ProviderStatusRaw, result.ErrorCode, result.ErrorMessage, result.ProviderSentAt);
            await _notificationRepository.UpdateAsync(resend, ct);
            _logger.LogInformation("Resent notification {SourceId} as {ResultId}.", source.Id, resend.Id);
            return new ResendResult(ResendOutcome.Sent, resend.Id, null);
        }
        catch (SmsProviderException ex)
        {
            // The send may have reached the provider; the claim stays so the same key never sends again.
            resend.RecordSendUnknown();
            await _notificationRepository.UpdateAsync(resend, ct);
            _logger.LogWarning(ex, "Resend of notification {SourceId} did not complete cleanly.", source.Id);
            return new ResendResult(ResendOutcome.ProviderUnavailable, resend.Id,
                "The message could not be confirmed sent; its outcome is unknown.");
        }
    }

    public async Task<ContentDisposalResult> DisposeContentAsync(int notificationId, CancellationToken ct)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, ct);
        if (notification is null)
        {
            return new ContentDisposalResult(ContentDisposalOutcome.NotFound, "Notification not found.");
        }

        if (notification.ContentRedacted)
        {
            return new ContentDisposalResult(ContentDisposalOutcome.AlreadyDisposed, "Content already disposed of.");
        }

        // Redact at the provider FIRST so the text is no longer retrievable there; then dispose locally.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            try
            {
                await _gateway.RedactContentAsync(notification.ProviderMessageSid, ct);
            }
            catch (SmsProviderException ex)
            {
                _logger.LogWarning(ex, "Could not redact content at the provider for notification {NotificationId}.",
                    notificationId);
                return new ContentDisposalResult(ContentDisposalOutcome.ProviderUnavailable,
                    "The provider could not be reached to dispose of the content. Please retry.");
            }
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, ct);
        _logger.LogInformation("Disposed of content for notification {NotificationId}.", notificationId);
        return new ContentDisposalResult(ContentDisposalOutcome.Disposed, null);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        // Ask the provider for messages sent FROM our configured number in the range, paging the whole range.
        var providerMessages = new List<ProviderMessageSummary>();
        int? page = null;
        string? pageToken = null;
        var pagesRead = 0;
        var truncated = false;

        while (true)
        {
            var result = await _gateway.ListSentFromAsync(_settings.FromNumber, from, to, page, pageToken,
                ReconciliationPageSize, ct);
            providerMessages.AddRange(result.Messages);
            pagesRead++;

            if (result.NextPage is null && string.IsNullOrEmpty(result.NextPageToken))
            {
                break; // provider signalled the last page
            }

            if (pagesRead >= MaxReconciliationPages)
            {
                truncated = true; // safety cap hit — the answer is partial and we say so
                _logger.LogWarning("Reconciliation stopped at the {MaxPages}-page safety cap.", MaxReconciliationPages);
                break;
            }

            page = result.NextPage;
            pageToken = result.NextPageToken;
        }

        // What eShop believes it sent: local notifications carrying a provider SID.
        var localWithSid = await _notificationRepository.ListAsync(
            new OrderNotificationsWithProviderSidSpecification(), ct);
        var localBySid = localWithSid
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var providerSids = new HashSet<string>(providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .Select(m => m.Sid));

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        foreach (var m in providerMessages)
        {
            if (!string.IsNullOrEmpty(m.Sid) && localBySid.TryGetValue(m.Sid, out var local))
            {
                matched.Add(new ReconciliationEntry(m.Sid, m.StatusRaw, m.DateSent, local.Id, local.OrderId,
                    local.DeliveryStatus.ToString()));
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry(m.Sid, m.StatusRaw, m.DateSent, null, null, null));
            }
        }

        // eShop-only: what we believe we sent in this window but the provider's From-filtered range does not show.
        // Reconciliation clock is the provider's send time; widen with creation time only where it is not yet known.
        var eShopOnly = localWithSid
            .Where(n => !providerSids.Contains(n.ProviderMessageSid!))
            .Where(n => (n.ProviderSentAt.HasValue && n.ProviderSentAt.Value >= from && n.ProviderSentAt.Value <= to)
                     || (!n.ProviderSentAt.HasValue && n.CreatedAt >= from && n.CreatedAt <= to))
            .Select(n => new ReconciliationEntry(n.ProviderMessageSid, n.ProviderStatusRaw, n.ProviderSentAt, n.Id,
                n.OrderId, n.DeliveryStatus.ToString()))
            .ToList();

        return new ReconciliationReport(from, to, providerMessages.Count,
            localBySid.Count, matched.Count, matched, providerOnly, eShopOnly, truncated, pagesRead);
    }
}
