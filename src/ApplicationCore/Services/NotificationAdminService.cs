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

public class NotificationAdminService : INotificationAdminService
{
    private readonly IRepository<SmsNotification> _notificationRepository;
    private readonly IReadRepository<Order> _orderRepository;
    private readonly ISmsProvider _smsProvider;
    private readonly IAppLogger<NotificationAdminService> _logger;

    public NotificationAdminService(
        IRepository<SmsNotification> notificationRepository,
        IReadRepository<Order> orderRepository,
        ISmsProvider smsProvider,
        IAppLogger<NotificationAdminService> logger)
    {
        _notificationRepository = notificationRepository;
        _orderRepository = orderRepository;
        _smsProvider = smsProvider;
        _logger = logger;
    }

    // ---- Resend ----------------------------------------------------------------------------

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));

        // Idempotency: a repeat under the same key returns the first result without sending again.
        var alreadyDone = await _notificationRepository.FirstOrDefaultAsync(
            new SmsNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (alreadyDone != null)
            return ResendResult.Produced(alreadyDone.Id, "Idempotent replay: no new message was sent.");

        var original = await _notificationRepository.FirstOrDefaultAsync(
            new SmsNotificationByIdSpecification(notificationId), cancellationToken);
        if (original is null)
            return ResendResult.NotFound();

        var body = await ResolveResendBodyAsync(original, cancellationToken);

        // Persist the resend row (carrying the key) before sending, so a same-key retry is caught.
        var resend = new SmsNotification(original.OwnerId, original.OrderId, original.Kind, original.Destination, body);
        resend.MarkAsResendOf(original.Id, idempotencyKey);
        await _notificationRepository.AddAsync(resend, cancellationToken);

        try
        {
            var sent = await _smsProvider.SendAsync(original.Destination, body, cancellationToken);
            resend.RecordSent(sent.Sid, sent.Status, sent.ErrorCode, sent.ErrorMessage, sent.DateSent);
        }
        catch (Exception ex)
        {
            resend.RecordSendFailure(ex.Message);
            _logger.LogWarning("Resend of notification {NotificationId} failed. {Error}", notificationId, ex.Message);
        }

        await _notificationRepository.UpdateAsync(resend, cancellationToken);
        return ResendResult.Produced(resend.Id);
    }

    private async Task<string> ResolveResendBodyAsync(SmsNotification original, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(original.Content))
            return original.Content!;

        // Content was disposed of; regenerate from the order so the resend still means something.
        var order = await _orderRepository.GetByIdAsync(original.OrderId, cancellationToken);
        return order != null
            ? SmsMessageTemplates.For(original.Kind, order)
            : $"eShopOnWeb: an update about your order #{original.OrderId}.";
    }

    // ---- Content disposal ------------------------------------------------------------------

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.FirstOrDefaultAsync(
            new SmsNotificationByIdSpecification(notificationId), cancellationToken);
        if (notification is null)
            return false;

        // Redact at the provider first so the text is no longer retrievable there, then clear locally.
        // The record that a message was sent, and what became of it, is left intact.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
            await _smsProvider.RedactBodyAsync(notification.ProviderMessageSid!, cancellationToken);

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        return true;
    }

    // ---- Reconciliation --------------------------------------------------------------------

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Provider's own record for its own sending number over the range (server-side sender filter).
        var providerMessages = await _smsProvider.ListSentMessagesAsync(from, to, cancellationToken);
        var providerInRange = providerMessages
            .Where(m => m.DateSent.HasValue && m.DateSent.Value >= from && m.DateSent.Value <= to)
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First());

        // What eShop believes it sent in the range.
        var eshopNotifications = await _notificationRepository.ListAsync(
            new SmsNotificationsBySentRangeSpecification(from, to), cancellationToken);

        // Bring eShop's own view up to date so the two columns are compared as of the same moment.
        await RefreshEShopStatusesAsync(eshopNotifications, cancellationToken);
        var eshopBySid = eshopNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var eshopOnly = new List<ReconciliationEntry>();

        foreach (var (sid, m) in providerInRange)
        {
            if (eshopBySid.TryGetValue(sid, out var n))
            {
                matched.Add(new ReconciliationEntry(
                    ProviderMessageSid: sid,
                    NotificationId: n.Id,
                    OrderId: n.OrderId,
                    ProviderStatus: m.Status,
                    EShopStatus: n.Status.ToString(),
                    Destination: PhoneMask.Mask(n.Destination),
                    DateSent: m.DateSent));
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry(
                    ProviderMessageSid: sid,
                    NotificationId: null,
                    OrderId: null,
                    ProviderStatus: m.Status,
                    EShopStatus: null,
                    Destination: PhoneMask.Mask(m.To),
                    DateSent: m.DateSent));
            }
        }

        foreach (var (sid, n) in eshopBySid)
        {
            if (providerInRange.ContainsKey(sid))
                continue;
            eshopOnly.Add(new ReconciliationEntry(
                ProviderMessageSid: sid,
                NotificationId: n.Id,
                OrderId: n.OrderId,
                ProviderStatus: null,
                EShopStatus: n.Status.ToString(),
                Destination: PhoneMask.Mask(n.Destination),
                DateSent: n.SentAt));
        }

        var sendingNumber = providerInRange.Values.Select(m => m.From).FirstOrDefault(f => !string.IsNullOrEmpty(f));

        return new ReconciliationReport(
            From: from,
            To: to,
            SendingNumber: sendingNumber != null ? PhoneMask.Mask(sendingNumber) : "n/a",
            ProviderCount: providerInRange.Count,
            EShopCount: eshopBySid.Count,
            MatchedCount: matched.Count,
            Matched: matched.OrderBy(e => e.DateSent).ToList(),
            ProviderOnly: providerOnly.OrderBy(e => e.DateSent).ToList(),
            EShopOnly: eshopOnly.OrderBy(e => e.DateSent).ToList());
    }

    private async Task RefreshEShopStatusesAsync(IReadOnlyList<SmsNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var n in notifications)
        {
            if (n.ProviderMessageSid is null || n.Status.IsTerminal())
                continue;
            try
            {
                var snapshot = await _smsProvider.FetchStatusAsync(n.ProviderMessageSid, cancellationToken);
                if (snapshot != null)
                {
                    n.UpdateFromProvider(snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage, snapshot.DateSent);
                    await _notificationRepository.UpdateAsync(n, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Reconcile: refreshing notification {NotificationId} failed. {Error}", n.Id, ex.Message);
            }
        }
    }
}
