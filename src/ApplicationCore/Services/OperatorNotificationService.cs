using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OperatorNotificationService : IOperatorNotificationService
{
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ITwilioMessagingClient _messagingClient;
    private readonly OrderSmsNotifier _notifier;
    private readonly IAppLogger<OperatorNotificationService> _logger;

    public OperatorNotificationService(
        IRepository<OrderNotification> notifications,
        IRepository<ContactNumber> contactNumbers,
        ITwilioMessagingClient messagingClient,
        OrderSmsNotifier notifier,
        IAppLogger<OperatorNotificationService> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _messagingClient = messagingClient;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new NotificationActionException("An idempotency key is required.");
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            throw new NotificationNotFoundException();
        }

        var existing = await _notifications.FirstOrDefaultAsync(
            new NotificationByResendIdempotencySpec(notificationId, idempotencyKey.Trim()), cancellationToken);
        if (existing is not null)
        {
            await _notifier.RefreshFromProviderAsync(existing, cancellationToken);
            return existing;
        }

        await _notifier.RefreshFromProviderAsync(original, cancellationToken);
        if (!original.DidNotReachShopper)
        {
            throw new NotificationActionException("Only messages that failed or were undelivered can be resent.");
        }

        if (original.ContentRedacted || string.IsNullOrWhiteSpace(original.Body))
        {
            throw new NotificationActionException("The original message content is no longer available to resend.");
        }

        if (!original.ContactNumberId.HasValue)
        {
            throw new NotificationActionException("The destination is no longer on file; nothing will be sent to it again.");
        }

        var contact = await _contactNumbers.GetByIdAsync(original.ContactNumberId.Value, cancellationToken);
        if (contact is null || !contact.BelongsTo(original.BuyerId))
        {
            throw new NotificationActionException("The destination is no longer on file; nothing will be sent to it again.");
        }

        var resend = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            contact.Id,
            contact.CanonicalNumber,
            original.Kind,
            original.Body,
            scheduledFor: null,
            resentFromNotificationId: original.Id,
            idempotencyKey: idempotencyKey.Trim());

        resend = await _notifications.AddAsync(resend, cancellationToken);

        try
        {
            var result = await _messagingClient.SendSmsAsync(contact.CanonicalNumber, original.Body, cancellationToken);
            if (result is null)
            {
                resend.RecordSendFailure();
            }
            else
            {
                resend.RecordProviderResult(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage);
            }
        }
        catch
        {
            _logger.LogWarning("Resend of notification {NotificationId} could not be handed to the provider.", original.Id);
            resend.RecordSendFailure();
        }

        await _notifications.UpdateAsync(resend, cancellationToken);
        return resend;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            throw new NotificationNotFoundException();
        }

        if (notification.HasProviderIdentity)
        {
            var updated = await _messagingClient.RedactMessageBodyAsync(notification.ProviderMessageSid!, cancellationToken);
            if (updated is null)
            {
                throw new NotificationActionException("The provider could not dispose of the message content.");
            }

            notification.RecordProviderResult(updated.Sid, updated.Status, updated.ErrorCode, updated.ErrorMessage);
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new BadRequestException("The reconciliation 'to' timestamp must be on or after 'from'.");
        }

        var providerMessages = await _messagingClient.ListMessagesFromConfiguredSenderAsync(from, to, cancellationToken);
        var localNotifications = await _notifications.ListAsync(new NotificationsCreatedBetweenSpec(from, to), cancellationToken);

        var localBySid = localNotifications
            .Where(n => n.HasProviderIdentity)
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var rows = new List<ReconciliationMessage>();
        var matched = 0;
        var providerOnly = 0;
        var seenSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in providerMessages)
        {
            if (string.IsNullOrWhiteSpace(provider.Sid))
            {
                continue;
            }

            seenSids.Add(provider.Sid);
            localBySid.TryGetValue(provider.Sid, out var local);
            if (local is not null)
            {
                matched++;
                rows.Add(ToRow(provider, local, "matched"));
            }
            else
            {
                providerOnly++;
                rows.Add(ToRow(provider, null, "provider_only"));
            }
        }

        var localOnly = 0;
        foreach (var local in localNotifications)
        {
            if (!local.HasProviderIdentity || !seenSids.Contains(local.ProviderMessageSid!))
            {
                localOnly++;
                rows.Add(new ReconciliationMessage(
                    local.ProviderMessageSid,
                    local.ProviderStatus,
                    local.BodyForDisplay,
                    _messagingClient.ConfiguredFromNumber,
                    null,
                    local.CreatedAt.ToString("O"),
                    null,
                    local.Id,
                    local.OrderId,
                    "local_only"));
            }
        }

        return new ReconciliationReport(
            from,
            to,
            _messagingClient.ConfiguredFromNumber,
            rows,
            matched,
            providerOnly,
            localOnly);
    }

    private static ReconciliationMessage ToRow(TwilioMessageSnapshot provider, OrderNotification? local, string alignment) =>
        new(
            provider.Sid,
            provider.Status,
            local?.ContentRedacted == true ? null : provider.Body,
            provider.From,
            provider.To,
            provider.DateCreated,
            provider.DateSent,
            local?.Id,
            local?.OrderId,
            alignment);
}
