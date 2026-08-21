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

public class NotificationOperatorService : INotificationOperatorService
{
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly IRepository<ResendIdempotencyRecord> _idempotency;
    private readonly IMessagingProviderClient _messaging;
    private readonly IAppLogger<NotificationOperatorService> _logger;

    public NotificationOperatorService(
        IRepository<OrderNotification> notifications,
        IRepository<ShopperContactNumber> contactNumbers,
        IRepository<ResendIdempotencyRecord> idempotency,
        IMessagingProviderClient messaging,
        IAppLogger<NotificationOperatorService> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _idempotency = idempotency;
        _messaging = messaging;
        _logger = logger;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new NotificationOperationException("An idempotency key is required.");
        }

        var existingKey = await _idempotency.FirstOrDefaultAsync(new ResendIdempotencyByKeySpecification(idempotencyKey), cancellationToken);
        if (existingKey != null)
        {
            var previous = await _notifications.GetByIdAsync(existingKey.ResultNotificationId, cancellationToken);
            if (previous != null)
            {
                return previous;
            }
        }

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (source is null)
        {
            throw new NotificationNotFoundException(notificationId);
        }

        if (!string.IsNullOrEmpty(source.ProviderSid))
        {
            try
            {
                var latest = await _messaging.FetchAsync(source.ProviderSid, cancellationToken);
                source.ApplyProviderOutcome(latest.Status, latest.ErrorCode, latest.ErrorCode is null ? null : $"Provider error {latest.ErrorCode}", latest.DateSent);
                await _notifications.UpdateAsync(source, cancellationToken);
            }
            catch (Exception)
            {
                _logger.LogWarning("Could not refresh source notification {NotificationId} before resend.", notificationId);
            }
        }

        if (!source.DidNotReachShopper())
        {
            throw new NotificationOperationException("Only messages that did not reach the shopper can be re-sent.");
        }

        if (string.IsNullOrEmpty(source.Body) || source.ContentRedacted)
        {
            throw new NotificationOperationException("The original message content is no longer available.");
        }

        var activeNumbers = await _contactNumbers.ListAsync(new ShopperContactNumbersSpecification(source.BuyerId), cancellationToken);
        if (activeNumbers.All(n => n.PhoneNumber != source.DestinationNumber))
        {
            throw new NotificationOperationException("The destination number is no longer on file for this shopper.");
        }

        var resend = new OrderNotification(
            source.OrderId,
            source.BuyerId,
            NotificationKind.Resend,
            source.DestinationNumber,
            source.Body,
            source.Id);

        await _notifications.AddAsync(resend, cancellationToken);

        try
        {
            var sent = await _messaging.SendAsync(source.DestinationNumber, source.Body, cancellationToken);
            if (string.IsNullOrEmpty(resend.ProviderSid))
            {
                resend.RecordProviderAcceptance(sent.Sid, sent.Status, sent.DateSent);
            }
            else
            {
                resend.ApplyProviderOutcome(sent.Status, sent.ErrorCode, sent.ErrorCode is null ? null : $"Provider error {sent.ErrorCode}", sent.DateSent);
            }
        }
        catch (Exception)
        {
            resend.RecordLocalFailure("The messaging provider did not accept the message.");
            _logger.LogWarning("Provider rejected resend notification {NotificationId}.", resend.Id);
        }

        await _notifications.UpdateAsync(resend, cancellationToken);
        await _idempotency.AddAsync(new ResendIdempotencyRecord(idempotencyKey, source.Id, resend.Id), cancellationToken);
        return resend;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            throw new NotificationNotFoundException(notificationId);
        }

        if (!string.IsNullOrEmpty(notification.ProviderSid))
        {
            await _messaging.RedactBodyAsync(notification.ProviderSid, cancellationToken);
        }

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Redacted content for notification {NotificationId}.", notificationId);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new NotificationOperationException("The 'to' timestamp must be on or after 'from'.");
        }

        var providerMessages = await _messaging.ListFromNumberAsync(from, to, cancellationToken);
        var local = await _notifications.ListAsync(new NotificationsByCreatedRangeSpecification(from, to), cancellationToken);

        var localBySid = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderSid))
            .GroupBy(n => n.ProviderSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var entries = new List<ReconciliationEntry>();
        var matchedSids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var provider in providerMessages)
        {
            if (localBySid.TryGetValue(provider.Sid, out var localMatch))
            {
                matchedSids.Add(provider.Sid);
                entries.Add(new ReconciliationEntry(
                    "matched",
                    localMatch.Id,
                    provider.Sid,
                    provider.Status,
                    localMatch.ProviderStatus,
                    localMatch.Kind.ToString()));
            }
            else
            {
                entries.Add(new ReconciliationEntry(
                    "providerOnly",
                    null,
                    provider.Sid,
                    provider.Status,
                    null,
                    null));
            }
        }

        foreach (var leftover in local.Where(n => string.IsNullOrEmpty(n.ProviderSid) || !matchedSids.Contains(n.ProviderSid!)))
        {
            entries.Add(new ReconciliationEntry(
                "eshopOnly",
                leftover.Id,
                leftover.ProviderSid,
                null,
                leftover.ProviderStatus,
                leftover.Kind.ToString()));
        }

        return new ReconciliationReport(from, to, _messaging.ConfiguredFromNumber, entries);
    }
}
