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

public class NotificationAdminService : INotificationAdminService
{
    private readonly IRepository<Notification> _notifications;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<NotificationAdminService> _logger;

    public NotificationAdminService(
        IRepository<Notification> notifications,
        ISmsGateway smsGateway,
        IAppLogger<NotificationAdminService> logger)
    {
        _notifications = notifications;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct = default)
    {
        // Replay: a repeat under the same key returns the earlier message; nothing is re-sent.
        var priorForKey = await _notifications.FirstOrDefaultAsync(new NotificationByResendKeySpecification(idempotencyKey), ct);
        if (priorForKey is not null)
            return new ResendResult(ResendStatus.ReplayedExisting, priorForKey.Id);

        var source = await _notifications.GetByIdAsync(notificationId, ct);
        if (source is null)
            return new ResendResult(ResendStatus.SourceNotFound, null);

        if (source.ContentDisposed || string.IsNullOrEmpty(source.Body))
            return new ResendResult(ResendStatus.ContentUnavailable, null);

        // Reserve the key by persisting the resend record BEFORE the send, so a concurrent repeat or
        // a transport-level retry under the same key finds it and does not produce a second message.
        var resend = new Notification(source.OrderId, source.BuyerId, source.Kind, source.ToPhoneNumber, source.Body!);
        resend.AttachResend(idempotencyKey, source.Id);
        resend = await _notifications.AddAsync(resend, ct);

        try
        {
            var result = await _smsGateway.SendAsync(source.ToPhoneNumber, source.Body!, ct);
            resend.RecordSent(result.ProviderMessageId, result.Status, result.ErrorCode, result.ErrorMessage);
        }
        catch (SmsGatewayException ex)
        {
            _logger.LogWarning("Resend of notification {0} failed (provider status {1}).", source.Id, ex.ProviderStatusCode);
            resend.MarkSendFailed(ex.Message);
        }

        await _notifications.UpdateAsync(resend, ct);
        return new ResendResult(ResendStatus.Sent, resend.Id);
    }

    public async Task<DisposeResult> DisposeContentAsync(int notificationId, CancellationToken ct = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, ct);
        if (notification is null)
            return new DisposeResult(DisposeStatus.NotFound);

        if (notification.ContentDisposed)
            return new DisposeResult(DisposeStatus.Ok);

        // Dispose the text at the provider FIRST so it is no longer retrievable there. If that throws,
        // it propagates (mapped to a 5xx) and the local copy is left intact — the content is not yet
        // disposed, and the caller learns it did not complete.
        if (notification.ProviderMessageId is not null)
            await _smsGateway.RedactContentAsync(notification.ProviderMessageId, ct);

        // The fact a message was sent and what became of it survives; only the text goes.
        notification.MarkContentDisposed();
        await _notifications.UpdateAsync(notification, ct);
        return new DisposeResult(DisposeStatus.Ok);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        // The provider is asked directly for only this application's sending number's messages.
        var providerRecords = await _smsGateway.ListSentMessagesAsync(from, to, ct);

        var allNotifications = await _notifications.ListAsync(ct);
        var eshopInRange = allNotifications
            .Where(n => n.ProviderMessageId is not null && n.CreatedAt >= from && n.CreatedAt <= to)
            .ToList();

        var providerBySid = providerRecords
            .GroupBy(p => p.ProviderMessageId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var eshopBySid = eshopInRange
            .GroupBy(n => n.ProviderMessageId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var entries = new List<ReconciliationEntry>();
        int matched = 0, providerOnly = 0, eshopOnly = 0;

        foreach (var p in providerRecords)
        {
            if (eshopBySid.TryGetValue(p.ProviderMessageId, out var n))
            {
                matched++;
                entries.Add(new ReconciliationEntry(p.ProviderMessageId, "matched", p.Status, n.Id, n.DeliveryStatus, n.OrderId));
            }
            else
            {
                providerOnly++;
                entries.Add(new ReconciliationEntry(p.ProviderMessageId, "provider-only", p.Status, null, null, null));
            }
        }

        foreach (var n in eshopInRange)
        {
            if (!providerBySid.ContainsKey(n.ProviderMessageId!))
            {
                eshopOnly++;
                entries.Add(new ReconciliationEntry(n.ProviderMessageId, "eshop-only", null, n.Id, n.DeliveryStatus, n.OrderId));
            }
        }

        return new ReconciliationReport(
            from, to,
            _smsGateway.SendingNumber,
            providerRecords.Count,
            eshopInRange.Count,
            matched, providerOnly, eshopOnly,
            entries);
    }
}
