using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class NotificationOperatorService : INotificationOperatorService
{
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<ResendIdempotencyRecord> _idempotency;
    private readonly ISmsGateway _smsGateway;
    private readonly OrderNotificationSender _notificationSender;
    private readonly string _fromNumber;

    public NotificationOperatorService(
        IRepository<OrderNotification> notifications,
        IRepository<ResendIdempotencyRecord> idempotency,
        ISmsGateway smsGateway,
        OrderNotificationSender notificationSender,
        ITwilioSettings twilioSettings)
    {
        _notifications = notifications;
        _idempotency = idempotency;
        _smsGateway = smsGateway;
        _notificationSender = notificationSender;
        _fromNumber = twilioSettings.FromNumber;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new OrderNotificationException(400, "An idempotency key is required.");
        }

        var existing = await _idempotency.FirstOrDefaultAsync(
            new ResendIdempotencyByKeySpec(notificationId, idempotencyKey), cancellationToken);
        if (existing is not null)
        {
            var previous = await _notifications.GetByIdAsync(existing.ResultNotificationId, cancellationToken);
            if (previous is not null)
            {
                return previous;
            }
        }

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (source is null)
        {
            throw new OrderNotificationException(404, "Notification not found.");
        }

        await _notificationSender.RefreshFromProviderAsync(source, cancellationToken);

        if (OrderNotificationSender.ReachedShopper(source.ProviderStatus))
        {
            throw new OrderNotificationException(409, "This message already reached the shopper.");
        }

        if (source.ContentDisposed || string.IsNullOrWhiteSpace(source.Body))
        {
            throw new OrderNotificationException(409, "Message content has been disposed and cannot be resent.");
        }

        var destination = await _notificationSender.GetDestinationAsync(source.BuyerId, cancellationToken);
        if (string.IsNullOrWhiteSpace(destination))
        {
            throw new OrderNotificationException(409, "The shopper has no contact number on file.");
        }

        var resent = await _notificationSender.SendImmediateAsync(
            source.OrderId,
            source.BuyerId,
            NotificationKind.Resend,
            source.Body,
            cancellationToken,
            resentFromNotificationId: source.Id,
            destinationOverride: destination);

        if (resent is null)
        {
            throw new OrderNotificationException(409, "The shopper has no contact number on file.");
        }

        await _idempotency.AddAsync(new ResendIdempotencyRecord(notificationId, idempotencyKey, resent.Id), cancellationToken);
        return resent;
    }

    public async Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            throw new OrderNotificationException(404, "Notification not found.");
        }

        if (!string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            try
            {
                await _smsGateway.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
            }
            catch (Exception ex) when (OrderNotificationSender.IsSendFailure(ex))
            {
                var status = (ex as SmsGatewayException)?.StatusCode;
                if (status != 404)
                {
                    throw new OrderNotificationException(502, "The provider could not dispose of the message content.");
                }
            }
        }

        notification.DisposeContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new OrderNotificationException(400, "'to' must be on or after 'from'.");
        }

        var providerList = await _smsGateway.ListFromConfiguredSenderAsync(from, to, cancellationToken);
        var local = await _notifications.ListAsync(new OrderNotificationsWithProviderSidSpec(), cancellationToken);

        var localBySid = local
            .Where(n => !string.IsNullOrWhiteSpace(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var providerBySid = providerList.Messages
            .Where(m => !string.IsNullOrWhiteSpace(m.Sid))
            .GroupBy(m => m.Sid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var eShopOnly = new List<ReconciliationEntry>();

        foreach (var (sid, provider) in providerBySid)
        {
            if (localBySid.TryGetValue(sid, out var eShop))
            {
                matched.Add(new ReconciliationEntry
                {
                    NotificationId = eShop.Id,
                    ProviderSid = sid,
                    ProviderStatus = provider.Status,
                    EShopStatus = eShop.ProviderStatus,
                    OrderId = eShop.OrderId,
                    DateSent = provider.DateSent
                });
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry
                {
                    ProviderSid = sid,
                    ProviderStatus = provider.Status,
                    DateSent = provider.DateSent
                });
            }
        }

        foreach (var (sid, eShop) in localBySid)
        {
            if (!providerBySid.ContainsKey(sid) && eShop.CreatedAt >= from && eShop.CreatedAt <= to)
            {
                eShopOnly.Add(new ReconciliationEntry
                {
                    NotificationId = eShop.Id,
                    ProviderSid = sid,
                    EShopStatus = eShop.ProviderStatus,
                    OrderId = eShop.OrderId
                });
            }
        }

        return new ReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = _fromNumber,
            Truncated = providerList.Truncated,
            Matched = matched,
            ProviderOnly = providerOnly,
            EShopOnly = eShopOnly
        };
    }
}
