using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan DeliveryFeedbackDelay = TimeSpan.FromDays(3);

    private static readonly HashSet<string> TerminalNegativeStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "failed",
        "undelivered"
    };

    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ISmsGateway _smsGateway;
    private readonly IOrderNotificationCommands _notificationCommands;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notifications,
        IRepository<ContactNumber> contactNumbers,
        ISmsGateway smsGateway,
        IOrderNotificationCommands notificationCommands,
        IAppLogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _smsGateway = smsGateway;
        _notificationCommands = notificationCommands;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        return NotifyAsync(order, OrderNotificationKind.OrderPlaced,
            $"Your eShopOnWeb order #{order.Id} has been placed. Thank you!",
            sendAt: null, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await NotifyAsync(order, OrderNotificationKind.OrderDispatched,
            $"Your eShopOnWeb order #{order.Id} is on its way.",
            sendAt: null, cancellationToken);

        await NotifyAsync(order, OrderNotificationKind.DeliveryFeedback,
            $"How did the delivery of your eShopOnWeb order #{order.Id} go?",
            sendAt: DateTimeOffset.UtcNow.Add(DeliveryFeedbackDelay), cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        await CancelPendingDeliveryFeedbackAsync(order.Id, cancellationToken);

        await NotifyAsync(order, OrderNotificationKind.OrderCancelled,
            $"Your eShopOnWeb order #{order.Id} has been cancelled.",
            sendAt: null, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, bool refreshFromProvider, CancellationToken cancellationToken = default)
    {
        return await ListForOrdersAsync(new[] { orderId }, refreshFromProvider, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrdersAsync(IEnumerable<int> orderIds, bool refreshFromProvider, CancellationToken cancellationToken = default)
    {
        var notifications = await _notifications.ListAsync(new NotificationsByOrderIdsSpecification(orderIds), cancellationToken);
        if (refreshFromProvider)
        {
            foreach (var notification in notifications)
            {
                await RefreshFromProviderAsync(notification, cancellationToken);
            }
        }

        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException("Notification was not found.");

        var existing = await _notifications.FirstOrDefaultAsync(
            new ResendNotificationByKeySpecification(notificationId, idempotencyKey), cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        await RefreshFromProviderAsync(original, cancellationToken);

        if (original.ContentDisposed || string.IsNullOrWhiteSpace(original.Body))
        {
            throw new InvalidOperationException("The original message content is no longer available to resend.");
        }

        var status = original.ProviderStatus ?? string.Empty;
        if (!TerminalNegativeStatuses.Contains(status) &&
            !string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Only failed or undelivered messages can be resent. Current status is '{status}'.");
        }

        var destination = await ResolveActiveDestinationAsync(original, cancellationToken);
        if (destination == null)
        {
            throw new InvalidOperationException("The destination number is no longer on file and cannot be messaged again.");
        }

        var resend = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            destination.Id,
            original.Kind,
            original.Body,
            sendAt: null,
            resendOfNotificationId: original.Id,
            idempotencyKey: idempotencyKey);

        await _notifications.AddAsync(resend, cancellationToken);
        await TrySendAsync(resend, destination.PhoneNumber, cancellationToken);
        return resend;
    }

    public async Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException("Notification was not found.");

        if (!string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            await RedactAtProviderAsync(notification.ProviderMessageSid, cancellationToken);
        }

        await _notificationCommands.PersistDisposalAsync(notificationId, cancellationToken);
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The 'to' timestamp must be on or after 'from'.");
        }

        var providerMessages = await _smsGateway.ListSentFromConfiguredNumberAsync(from, to, cancellationToken);
        var local = await _notifications.ListAsync(new NotificationsWithProviderSidInRangeSpecification(from, to), cancellationToken);

        var localBySid = local
            .Where(n => !string.IsNullOrWhiteSpace(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrWhiteSpace(m.Sid))
            .GroupBy(m => m.Sid, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matched = new List<ReconciledNotification>();
        var providerOnly = new List<ReconciledNotification>();
        var applicationOnly = new List<ReconciledNotification>();

        foreach (var provider in providerBySid.Values)
        {
            if (localBySid.TryGetValue(provider.Sid, out var notification))
            {
                matched.Add(ToReconciled(notification, provider));
            }
            else
            {
                providerOnly.Add(new ReconciledNotification
                {
                    ProviderMessageSid = provider.Sid,
                    Status = provider.Status,
                    DateSent = provider.DateSent,
                    DateCreated = provider.DateCreated
                });
            }
        }

        foreach (var notification in localBySid.Values)
        {
            if (!providerBySid.ContainsKey(notification.ProviderMessageSid!))
            {
                applicationOnly.Add(ToReconciled(notification, provider: null));
            }
        }

        return new NotificationReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = _smsGateway.ConfiguredFromNumber,
            Matched = matched,
            ProviderOnly = providerOnly,
            ApplicationOnly = applicationOnly
        };
    }

    private async Task NotifyAsync(
        Order order,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var destinations = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
            if (destinations.Count == 0)
            {
                _logger.LogInformation("Skipping {Kind} notification for order {OrderId}; buyer has no number on file", kind, order.Id);
                return;
            }

            foreach (var destination in destinations)
            {
                var notification = new OrderNotification(order.Id, order.BuyerId, destination.Id, kind, body, sendAt);
                await _notifications.AddAsync(notification, cancellationToken);
                await TrySendAsync(notification, destination.PhoneNumber, cancellationToken);
            }
        }
        catch (Exception)
        {
            _logger.LogWarning("Notification {Kind} for order {OrderId} could not be completed", kind, order.Id);
        }
    }

    private async Task TrySendAsync(OrderNotification notification, string destination, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _smsGateway.SendAsync(new SmsSendRequest(destination, notification.Body ?? string.Empty, notification.SendAt), cancellationToken);
            notification.RecordProviderAcceptance(result.Sid, result.Status ?? (notification.SendAt.HasValue ? "scheduled" : "queued"), result.ErrorCode);
        }
        catch (SmsProviderException ex)
        {
            notification.RecordSendFailure(ex.ProviderErrorCode);
            _logger.LogWarning("Provider send failed for notification {NotificationId} of order {OrderId} with HTTP {StatusCode}", notification.Id, notification.OrderId, ex.StatusCode);
        }
        catch (Exception)
        {
            notification.RecordSendFailure(null);
            _logger.LogWarning("Provider send failed for notification {NotificationId} of order {OrderId}", notification.Id, notification.OrderId);
        }

        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    private async Task CancelPendingDeliveryFeedbackAsync(int orderId, CancellationToken cancellationToken)
    {
        try
        {
            var pending = await _notifications.ListAsync(new ScheduledFeedbackByOrderSpecification(orderId), cancellationToken);
            foreach (var notification in pending)
            {
                await RefreshFromProviderAsync(notification, cancellationToken);
                if (!notification.IsScheduledPending() || string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
                {
                    continue;
                }

                try
                {
                    var cancelled = await _smsGateway.CancelAsync(notification.ProviderMessageSid, cancellationToken);
                    notification.ApplyProviderDeliveryState(cancelled.Status ?? "canceled", cancelled.ErrorCode);
                    await _notifications.UpdateAsync(notification, cancellationToken);
                }
                catch (Exception)
                {
                    _logger.LogWarning("Could not cancel scheduled follow-up {NotificationId} for order {OrderId}", notification.Id, orderId);
                }
            }
        }
        catch (Exception)
        {
            _logger.LogWarning("Failed while cancelling scheduled follow-up messages for order {OrderId}", orderId);
        }
    }

    private async Task RedactAtProviderAsync(string providerSid, CancellationToken cancellationToken)
    {
        SmsProviderException? lastConflict = null;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                var redacted = await _smsGateway.RedactBodyAsync(providerSid, cancellationToken);
                if (string.IsNullOrEmpty(redacted.Body))
                {
                    return;
                }

                var fetched = await _smsGateway.FetchAsync(providerSid, cancellationToken);
                if (string.IsNullOrEmpty(fetched.Body))
                {
                    return;
                }
            }
            catch (SmsProviderException ex) when (ex.StatusCode == 409 || ex.ProviderErrorCode == 20409)
            {
                lastConflict = ex;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        throw lastConflict ?? new SmsProviderException("Redact message", 409, 20409);
    }

    private async Task RefreshFromProviderAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            return;
        }

        try
        {
            var provider = await _smsGateway.FetchAsync(notification.ProviderMessageSid, cancellationToken);
            notification.ApplyProviderDeliveryState(provider.Status ?? notification.ProviderStatus ?? string.Empty, provider.ErrorCode);
            if (notification.ContentDisposed || provider.Body == string.Empty)
            {
                notification.MarkContentDisposed();
            }
        }
        catch (Exception)
        {
            _logger.LogWarning("Could not refresh provider status for notification {NotificationId}", notification.Id);
        }
    }

    private async Task<ContactNumber?> ResolveActiveDestinationAsync(OrderNotification original, CancellationToken cancellationToken)
    {
        if (original.ContactNumberId.HasValue)
        {
            var byId = await _contactNumbers.GetByIdAsync(original.ContactNumberId.Value, cancellationToken);
            if (byId != null && byId.BuyerId == original.BuyerId)
            {
                return byId;
            }
        }

        var remaining = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(original.BuyerId), cancellationToken);
        return remaining.FirstOrDefault();
    }

    private static ReconciledNotification ToReconciled(OrderNotification notification, SmsMessage? provider)
    {
        return new ReconciledNotification
        {
            NotificationId = notification.Id,
            ProviderMessageSid = provider?.Sid ?? notification.ProviderMessageSid,
            Status = provider?.Status ?? notification.ProviderStatus,
            OrderId = notification.OrderId,
            DateSent = provider?.DateSent,
            DateCreated = provider?.DateCreated ?? notification.CreatedAt
        };
    }
}
