using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Sms;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class NotificationOperationsService : INotificationOperationsService
{
    private readonly IRepository<SmsNotification> _notificationRepository;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<NotificationOperationsService> _logger;

    public NotificationOperationsService(
        IRepository<SmsNotification> notificationRepository,
        ISmsGateway smsGateway,
        IAppLogger<NotificationOperationsService> logger)
    {
        _notificationRepository = notificationRepository;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task<ResendOutcome> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        // Idempotency: a repeat under the same key returns the earlier result and sends nothing further.
        var priorForKey = await _notificationRepository.FirstOrDefaultAsync(
            new SmsNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (priorForKey is not null)
        {
            _logger.LogInformation($"Resend replayed for idempotency key; returning existing notification {priorForKey.Id}.");
            return new ResendOutcome(SourceFound: true, Result: priorForKey, Replayed: true);
        }

        var source = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (source is null)
        {
            return ResendOutcome.NotFound();
        }

        if (string.IsNullOrEmpty(source.Body))
        {
            throw new NotificationContentDisposedException(
                $"Notification {notificationId} has had its content disposed of and cannot be re-sent.");
        }

        var resend = new SmsNotification(source.BuyerId, source.OrderId, NotificationType.Resend,
            source.ToPhoneNumber, source.Body, idempotencyKey: idempotencyKey, resendOfNotificationId: source.Id);

        try
        {
            var sent = await _smsGateway.SendAsync(source.ToPhoneNumber, source.Body, cancellationToken);
            resend.RecordProviderAccepted(sent.Sid, sent.Status, sent.ErrorCode, sent.ErrorMessage);
        }
        catch (SmsGatewayException ex)
        {
            // The re-send is still recorded (under this key) so a repeat does not send again.
            resend.RecordSendFailure(ex.Message);
            _logger.LogWarning($"Resend of notification {source.Id} could not be sent; recorded as failed.");
        }

        resend = await _notificationRepository.AddAsync(resend, cancellationToken);
        return new ResendOutcome(SourceFound: true, Result: resend, Replayed: false);
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return false;
        }

        // Dispose of the text at the provider so it is no longer retrievable there either. The record of the
        // message and its outcome survives.
        if (notification.ProviderMessageSid is not null)
        {
            await _smsGateway.RedactContentAsync(notification.ProviderMessageSid, cancellationToken);
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation($"Disposed of content for notification {notificationId}.");
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider for its own record of messages sent from our configured number over the range.
        var providerMessages = await _smsGateway.ListSentFromConfiguredNumberAsync(from, to, cancellationToken);

        // What eShop believes it sent over the range (records that reached the provider, i.e. have a sid).
        var eShopNotifications = await _notificationRepository.ListAsync(
            new SmsNotificationsCreatedBetweenSpecification(from, to), cancellationToken);

        var providerBySid = providerMessages
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First());
        var eShopBySid = eShopNotifications
            .Where(n => n.ProviderMessageSid is not null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var eShopOnly = new List<ReconciliationEntry>();

        foreach (var sid in providerBySid.Keys.Union(eShopBySid.Keys))
        {
            var inProvider = providerBySid.TryGetValue(sid, out var providerMessage);
            var inEShop = eShopBySid.TryGetValue(sid, out var notification);

            var entry = new ReconciliationEntry(
                Sid: sid,
                InProvider: inProvider,
                InEShop: inEShop,
                ProviderStatus: providerMessage?.Status,
                EShopStatus: notification?.Status,
                NotificationId: notification?.Id,
                OrderId: notification?.OrderId);

            if (inProvider && inEShop) matched.Add(entry);
            else if (inProvider) providerOnly.Add(entry);
            else eShopOnly.Add(entry);
        }

        return new ReconciliationReport(
            From: from,
            To: to,
            FromNumber: _smsGateway.SendingNumber,
            ProviderCount: providerBySid.Count,
            EShopCount: eShopBySid.Count,
            MatchedCount: matched.Count,
            Matched: matched,
            ProviderOnly: providerOnly,
            EShopOnly: eShopOnly);
    }
}
