using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class NotificationOperatorService : INotificationOperatorService
{
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<NotificationOperatorService> _logger;
    private readonly ITwilioConfiguration _twilioConfiguration;

    public NotificationOperatorService(
        IRepository<OrderNotification> notifications,
        IRepository<ShopperContactNumber> contactNumbers,
        ISmsGateway smsGateway,
        IAppLogger<NotificationOperatorService> logger,
        ITwilioConfiguration twilioConfiguration)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _smsGateway = smsGateway;
        _logger = logger;
        _twilioConfiguration = twilioConfiguration;
    }

    public async Task<OrderNotification> ResendAsync(
        int notificationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            throw new KeyNotFoundException("Notification was not found.");
        }

        var existing = await _notifications.FirstOrDefaultAsync(
            new ResendNotificationByIdempotencySpecification(original.Id, idempotencyKey.Trim()),
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var contact = await _contactNumbers.GetByIdAsync(original.ContactNumberId, cancellationToken);
        if (contact is null || contact.IsDeleted)
        {
            throw new OrderTransitionException("The destination number is no longer registered; the message was not sent.");
        }

        var body = original.ResolveBodyForResend();
        var resent = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            contact.Id,
            contact.CanonicalNumber,
            original.Kind,
            body,
            scheduledSendAt: null,
            resendOfNotificationId: original.Id,
            idempotencyKey: idempotencyKey.Trim());

        try
        {
            var snapshot = await _smsGateway.SendAsync(
                new SmsSendRequest(contact.CanonicalNumber, body),
                cancellationToken);
            resent.RecordProviderAccepted(snapshot.Sid, snapshot.Status, snapshot.DateSent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Resend of notification {NotificationId} was accepted locally but the provider call failed: {Message}", original.Id, ex.Message);
            resent.RecordSendFailure("failed", errorCode: null, errorMessage: "The provider rejected or could not accept the message.");
        }

        return await _notifications.AddAsync(resent, cancellationToken);
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
            var updated = await _smsGateway.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
            if (updated is not null)
            {
                notification.ApplyProviderState(updated.Status, updated.ErrorCode, updated.ErrorMessage, updated.DateSent);
            }
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The 'to' timestamp must be on or after 'from'.");
        }

        var fromNumber = _twilioConfiguration.FromNumber;
        if (string.IsNullOrWhiteSpace(fromNumber))
        {
            throw new InvalidOperationException("Twilio:FromNumber is not configured.");
        }

        var providerMessages = await _smsGateway.ListSentFromAsync(fromNumber, from, to, cancellationToken);
        var local = await _notifications.ListAsync(new NotificationsInRangeSpecification(from, to), cancellationToken);

        var localBySid = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var providerBySid = providerMessages
            .GroupBy(m => m.Sid, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var eshopOnly = new List<ReconciliationEntry>();

        foreach (var (sid, provider) in providerBySid)
        {
            if (localBySid.TryGetValue(sid, out var ours))
            {
                matched.Add(ToEntry(ours, provider));
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry(
                    NotificationId: null,
                    ProviderMessageSid: provider.Sid,
                    Source: "provider",
                    Status: provider.Status,
                    DateSent: provider.DateSent ?? provider.DateCreated,
                    BodyPreview: Preview(provider.Body)));
            }
        }

        foreach (var ours in local)
        {
            if (!string.IsNullOrEmpty(ours.ProviderMessageSid) && providerBySid.ContainsKey(ours.ProviderMessageSid))
            {
                continue;
            }

            eshopOnly.Add(ToEntry(ours, provider: null));
        }

        return new ReconciliationReport(from, to, fromNumber, matched, providerOnly, eshopOnly);
    }

    private static ReconciliationEntry ToEntry(OrderNotification ours, SmsMessageSnapshot? provider)
    {
        return new ReconciliationEntry(
            NotificationId: ours.Id.ToString(),
            ProviderMessageSid: ours.ProviderMessageSid ?? provider?.Sid,
            Source: provider is null ? "eshop" : "matched",
            Status: provider?.Status ?? ours.ProviderStatus,
            DateSent: provider?.DateSent ?? ours.ProviderDateSent ?? ours.CreatedAt,
            BodyPreview: ours.ContentRedacted ? null : Preview(ours.Body));
    }

    private static string? Preview(string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return body;
        }

        return body.Length <= 80 ? body : body[..80] + "...";
    }
}
