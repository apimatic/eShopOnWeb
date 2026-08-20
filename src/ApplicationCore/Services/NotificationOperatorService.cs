using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class NotificationOperatorService : INotificationOperatorService
{
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<NotificationResendAttempt> _resendAttempts;
    private readonly IRepository<Order> _orders;
    private readonly ISmsNotificationGateway _gateway;
    private readonly OrderNotificationPublisher _publisher;
    private readonly IAppLogger<NotificationOperatorService> _logger;

    public NotificationOperatorService(
        IRepository<OrderNotification> notifications,
        IRepository<NotificationResendAttempt> resendAttempts,
        IRepository<Order> orders,
        ISmsNotificationGateway gateway,
        OrderNotificationPublisher publisher,
        IAppLogger<NotificationOperatorService> logger)
    {
        _notifications = notifications;
        _resendAttempts = resendAttempts;
        _orders = orders;
        _gateway = gateway;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ResendNotificationResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var existingAttempt = await _resendAttempts.FirstOrDefaultAsync(
            new NotificationResendAttemptSpec(notificationId, idempotencyKey), cancellationToken);
        if (existingAttempt != null)
        {
            var existing = await _notifications.GetByIdAsync(existingAttempt.ResultNotificationId, cancellationToken)
                ?? throw new NotificationNotFoundException(existingAttempt.ResultNotificationId);
            await _publisher.RefreshProviderStateAsync(existing, cancellationToken);
            return new ResendNotificationResult(existing, true);
        }

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotificationNotFoundException(notificationId);

        if (source.ContentRedacted || string.IsNullOrWhiteSpace(source.Body))
        {
            throw new InvalidOperationException("The message content is no longer available to resend.");
        }

        if (string.IsNullOrWhiteSpace(source.Destination))
        {
            throw new InvalidOperationException("The original destination is no longer available to resend.");
        }

        if (!await _publisher.IsDestinationStillRegisteredAsync(source.BuyerId, source.Destination, cancellationToken))
        {
            throw new InvalidOperationException("The destination is no longer on file and cannot be messaged.");
        }

        var order = await _orders.GetByIdAsync(source.OrderId, cancellationToken)
            ?? throw new OrderNotFoundException(source.OrderId);

        var resent = await _publisher.SendToDestinationAsync(
            order,
            NotificationKinds.Resend,
            source.Body,
            source.Destination,
            sendAt: null,
            sourceNotificationId: source.Id,
            cancellationToken);

        var attempt = new NotificationResendAttempt(source.Id, idempotencyKey, resent.Id);
        await _resendAttempts.AddAsync(attempt, cancellationToken);

        return new ResendNotificationResult(resent, false);
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotificationNotFoundException(notificationId);

        if (!string.IsNullOrEmpty(notification.ProviderSid))
        {
            try
            {
                await _gateway.RedactBodyAsync(notification.ProviderSid, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Provider redaction failed for notification {NotificationId}: {Error}", notification.Id, ex.GetType().Name);
                throw new SmsProviderException("The provider could not dispose of the message content.", innerException: ex);
            }
        }

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (to < from)
        {
            throw new ArgumentException("The 'to' timestamp must be on or after 'from'.");
        }

        var providerList = await _gateway.ListSentFromConfiguredNumberAsync(from, to, cancellationToken);
        var truncated = providerList.Truncated;

        var providerBySid = providerList.Messages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First());

        var localInRange = await _notifications.ListAsync(new OrderNotificationsCreatedInRangeSpec(from, to), cancellationToken);
        var localByProviderSid = providerBySid.Count == 0
            ? new List<OrderNotification>()
            : await _notifications.ListAsync(new OrderNotificationsByProviderSidsSpec(providerBySid.Keys), cancellationToken);

        var localBySid = localByProviderSid
            .Where(n => !string.IsNullOrEmpty(n.ProviderSid))
            .GroupBy(n => n.ProviderSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var rows = new List<ReconciliationRow>();
        var seenSids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var provider in providerBySid.Values)
        {
            seenSids.Add(provider.Sid);
            if (localBySid.TryGetValue(provider.Sid, out var local))
            {
                rows.Add(new ReconciliationRow(
                    provider.Sid,
                    "matched",
                    provider.Status,
                    local.ProviderStatus,
                    local.Id,
                    provider.DateSent,
                    BodyPresence(provider.Body)));
            }
            else
            {
                rows.Add(new ReconciliationRow(
                    provider.Sid,
                    "providerOnly",
                    provider.Status,
                    null,
                    null,
                    provider.DateSent,
                    BodyPresence(provider.Body)));
            }
        }

        foreach (var local in localInRange)
        {
            if (!string.IsNullOrEmpty(local.ProviderSid) && seenSids.Contains(local.ProviderSid))
            {
                continue;
            }

            rows.Add(new ReconciliationRow(
                local.ProviderSid,
                "applicationOnly",
                null,
                local.ProviderStatus,
                local.Id,
                null,
                local.ContentRedacted ? "redacted" : BodyPresence(local.Body)));
        }

        return new ReconciliationReport(from, to, truncated, rows);
    }

    private static string BodyPresence(string? body)
    {
        return string.IsNullOrEmpty(body) ? "absent" : "present";
    }
}
