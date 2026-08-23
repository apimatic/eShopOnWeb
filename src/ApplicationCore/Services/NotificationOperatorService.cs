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
    private readonly IRepository<BuyerContactNumber> _contactNumbers;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<NotificationOperatorService> _logger;

    public NotificationOperatorService(
        IRepository<OrderNotification> notifications,
        IRepository<BuyerContactNumber> contactNumbers,
        ISmsGateway smsGateway,
        IAppLogger<NotificationOperatorService> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.");
        }

        var existing = await _notifications.FirstOrDefaultAsync(
            new OrderNotificationResendIdempotencySpecification(notificationId, idempotencyKey),
            cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new ArgumentException($"Notification {notificationId} was not found.");

        await RefreshAsync(source, cancellationToken);

        var status = source.ProviderStatus?.ToLowerInvariant();
        if (status is "delivered" or "sent" or "queued" or "scheduled" or "accepted" or "sending")
        {
            throw new OrderTransitionException("Only messages that did not reach the shopper can be resent.");
        }

        var numbers = await _contactNumbers.ListAsync(
            new BuyerContactNumbersSpecification(source.BuyerId),
            cancellationToken);
        if (!numbers.Any(n => n.CanonicalNumber == source.DestinationNumber))
        {
            throw new OrderTransitionException("The destination is no longer on file and cannot be messaged.");
        }

        var body = source.BodyRedacted || string.IsNullOrWhiteSpace(source.Body)
            ? $"eShopOnWeb: an update about your order #{source.OrderId}."
            : source.Body!;

        var resend = new OrderNotification(
            source.OrderId,
            source.BuyerId,
            OrderNotificationKind.Resend,
            source.DestinationNumber,
            body);
        resend.MarkAsResend(source.Id, idempotencyKey);
        await _notifications.AddAsync(resend, cancellationToken);

        try
        {
            var result = await _smsGateway.SendAsync(
                new SmsSendRequest(source.DestinationNumber, body, SendAt: null),
                cancellationToken);
            resend.AttachProviderResult(
                result.ProviderSid,
                result.OutcomeUnknown ? "unknown" : result.Status,
                result.ErrorCode,
                result.ErrorMessage,
                sendAt: null);
        }
        catch (Exception ex)
        {
            resend.MarkSendFailed("The provider did not accept the message.");
            _logger.LogWarning("Resend of notification {NotificationId} failed. {ExceptionType}", notificationId, ex.GetType().Name);
        }

        await _notifications.UpdateAsync(resend, cancellationToken);
        return resend;
    }

    public async Task<OrderNotification> RedactContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new ArgumentException($"Notification {notificationId} was not found.");

        if (!string.IsNullOrWhiteSpace(notification.ProviderSid))
        {
            SmsMessageSnapshot? snapshot;
            try
            {
                snapshot = await _smsGateway.RedactBodyAsync(notification.ProviderSid, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Provider redaction failed for notification {NotificationId}. {ExceptionType}", notificationId, ex.GetType().Name);
                throw new TwilioUnavailableException("The provider could not dispose of the message content.");
            }

            notification.MarkRedacted(snapshot?.Body);
            if (snapshot?.Status != null)
            {
                notification.RefreshFromProvider(snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage, body: null);
                notification.MarkRedacted(snapshot.Body);
            }
        }
        else
        {
            notification.MarkRedacted(remainingBody: null);
        }

        await _notifications.UpdateAsync(notification, cancellationToken);
        return notification;
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (to < from)
        {
            throw new ArgumentException("The 'to' timestamp must be on or after 'from'.");
        }

        var providerMessages = await _smsGateway.ListSentFromConfiguredNumberAsync(from, to, cancellationToken);
        var local = await _notifications.ListAsync(
            new OrderNotificationsByCreatedRangeSpecification(from, to),
            cancellationToken);

        var localBySid = local
            .Where(n => !string.IsNullOrWhiteSpace(n.ProviderSid))
            .GroupBy(n => n.ProviderSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var providerSids = new HashSet<string>(
            providerMessages.Where(m => !string.IsNullOrWhiteSpace(m.Sid)).Select(m => m.Sid!),
            StringComparer.Ordinal);

        var matched = new List<ReconciliationMatch>();
        var providerOnly = new List<SmsMessageSnapshot>();
        foreach (var message in providerMessages)
        {
            if (!string.IsNullOrWhiteSpace(message.Sid) && localBySid.TryGetValue(message.Sid, out var notification))
            {
                matched.Add(new ReconciliationMatch(message.Sid, notification.Id));
            }
            else
            {
                providerOnly.Add(message);
            }
        }

        var localOnly = local
            .Where(n => string.IsNullOrWhiteSpace(n.ProviderSid) || !providerSids.Contains(n.ProviderSid))
            .ToList();

        return new NotificationReconciliationReport(
            from,
            to,
            providerMessages.Count,
            local.Count,
            matched,
            providerOnly,
            localOnly,
            Truncated: false);
    }

    private async Task RefreshAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(notification.ProviderSid))
        {
            return;
        }

        try
        {
            var snapshot = await _smsGateway.FetchAsync(notification.ProviderSid, cancellationToken);
            if (snapshot == null)
            {
                return;
            }

            notification.RefreshFromProvider(
                snapshot.Status ?? notification.ProviderStatus,
                snapshot.ErrorCode,
                snapshot.ErrorMessage,
                notification.BodyRedacted ? null : snapshot.Body);
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not refresh notification {NotificationId}. {ExceptionType}", notification.Id, ex.GetType().Name);
        }
    }
}
