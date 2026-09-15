using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OperatorNotificationService : IOperatorNotificationService
{
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<NotificationIdempotencyRecord> _idempotencyRecords;
    private readonly IRepository<Order> _orders;
    private readonly IOrderNotificationSender _notificationSender;
    private readonly ISmsMessageGateway _smsGateway;
    private readonly ITwilioSendingNumberAccessor _sendingNumberAccessor;
    private readonly IAppLogger<OperatorNotificationService> _logger;

    public OperatorNotificationService(
        IRepository<OrderNotification> notifications,
        IRepository<NotificationIdempotencyRecord> idempotencyRecords,
        IRepository<Order> orders,
        IOrderNotificationSender notificationSender,
        ISmsMessageGateway smsGateway,
        ITwilioSendingNumberAccessor sendingNumberAccessor,
        IAppLogger<OperatorNotificationService> logger)
    {
        _notifications = notifications;
        _idempotencyRecords = idempotencyRecords;
        _orders = orders;
        _notificationSender = notificationSender;
        _smsGateway = smsGateway;
        _sendingNumberAccessor = sendingNumberAccessor;
        _logger = logger;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new InvalidNotificationOperationException("An idempotency key is required.");
        }

        var existing = await _idempotencyRecords.FirstOrDefaultAsync(
            new NotificationIdempotencySpecification(notificationId, idempotencyKey.Trim()),
            cancellationToken);
        if (existing != null)
        {
            var previous = await _notifications.GetByIdAsync(existing.ResultNotificationId, cancellationToken);
            if (previous != null)
            {
                return previous;
            }
        }

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (source == null)
        {
            throw new NotificationNotFoundException();
        }

        await _notificationSender.SyncFromProviderAsync(source, cancellationToken);

        if (source.HasReachedShopper())
        {
            throw new InvalidNotificationOperationException("The message already reached the shopper and will not be re-sent.");
        }

        if (IsStillInProviderQueue(source.ProviderStatus))
        {
            throw new InvalidNotificationOperationException("The message is still in flight with the provider and cannot be re-sent yet.");
        }

        if (source.ContentRedacted || string.IsNullOrWhiteSpace(source.Body))
        {
            throw new InvalidNotificationOperationException("The message content has been disposed of and cannot be re-sent.");
        }

        var order = await _orders.GetByIdAsync(source.OrderId, cancellationToken);
        if (order == null)
        {
            throw new OrderNotFoundException();
        }

        if (order.Status == OrderStatus.Cancelled && source.Kind == NotificationKind.DeliveryFollowUp)
        {
            throw new InvalidNotificationOperationException("A delivery follow-up cannot be re-sent for a cancelled order.");
        }

        var resent = await _notificationSender.TryNotifyAsync(
            order,
            NotificationKind.Resend,
            source.Body!,
            sourceNotificationId: source.Id,
            cancellationToken: cancellationToken);

        if (resent == null)
        {
            throw new InvalidNotificationOperationException("The shopper has no contact number on file, so the message was not re-sent.");
        }

        await _idempotencyRecords.AddAsync(
            new NotificationIdempotencyRecord(source.Id, idempotencyKey.Trim(), resent.Id),
            cancellationToken);

        return resent;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification == null)
        {
            throw new NotificationNotFoundException();
        }

        if (!string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            try
            {
                var result = await _smsGateway.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
                notification.ApplyProviderState(result.Status, result.ErrorCode, result.ErrorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to redact provider content for notification {NotificationId}: {Message}", notification.Id, ex.Message);
                throw new InvalidNotificationOperationException("The provider could not dispose of the message content.");
            }
        }

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new InvalidNotificationOperationException("The reconciliation range is invalid.");
        }

        var fromNumber = _sendingNumberAccessor.FromNumber;
        var providerMessages = await _smsGateway.ListSentFromAsync(fromNumber, from, to, cancellationToken);
        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrWhiteSpace(m.Sid))
            .GroupBy(m => m.Sid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var localInRange = await _notifications.ListAsync(new OrderNotificationsByCreatedRangeSpecification(from, to), cancellationToken);
        var localBySid = new Dictionary<string, OrderNotification>(StringComparer.Ordinal);
        foreach (var notification in localInRange)
        {
            if (!string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
            {
                localBySid[notification.ProviderMessageSid] = notification;
            }
        }

        var missingLocalSids = providerBySid.Keys.Where(sid => !localBySid.ContainsKey(sid)).ToArray();
        if (missingLocalSids.Length > 0)
        {
            var extraLocal = await _notifications.ListAsync(new OrderNotificationsByProviderSidsSpecification(missingLocalSids), cancellationToken);
            foreach (var notification in extraLocal)
            {
                if (!string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
                {
                    localBySid[notification.ProviderMessageSid] = notification;
                }
            }
        }

        var matched = new List<ReconciledNotification>();
        var providerOnly = new List<ReconciledNotification>();
        foreach (var provider in providerBySid.Values)
        {
            if (localBySid.TryGetValue(provider.Sid!, out var local))
            {
                matched.Add(ToReconciled(local, provider));
            }
            else
            {
                providerOnly.Add(new ReconciledNotification(
                    null,
                    provider.Sid,
                    provider.Status,
                    provider.Body,
                    provider.DateSent,
                    provider.DateCreated));
            }
        }

        var applicationOnly = new List<ReconciledNotification>();
        foreach (var local in localInRange)
        {
            if (string.IsNullOrWhiteSpace(local.ProviderMessageSid) || !providerBySid.ContainsKey(local.ProviderMessageSid))
            {
                if (!string.IsNullOrWhiteSpace(local.ProviderMessageSid) && matched.Any(m => m.NotificationId == local.Id))
                {
                    continue;
                }

                applicationOnly.Add(ToReconciled(local, null));
            }
        }

        return new NotificationReconciliationReport(from, to, matched, providerOnly, applicationOnly);
    }

    private static ReconciledNotification ToReconciled(OrderNotification local, SmsMessageResult? provider)
    {
        return new ReconciledNotification(
            local.Id,
            provider?.Sid ?? local.ProviderMessageSid,
            provider?.Status ?? local.ProviderStatus,
            local.ContentRedacted ? null : (provider?.Body ?? local.Body),
            provider?.DateSent,
            provider?.DateCreated ?? local.CreatedAt.ToString("o"));
    }

    private static bool IsStillInProviderQueue(string? status)
    {
        return string.Equals(status, "queued", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "accepted", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "scheduled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "sending", StringComparison.OrdinalIgnoreCase);
    }
}
