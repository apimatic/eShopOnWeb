using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly IRepository<Order> _orders;
    private readonly ISmsMessageGateway _smsGateway;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notifications,
        IRepository<ShopperContactNumber> contactNumbers,
        IRepository<Order> orders,
        ISmsMessageGateway smsGateway,
        IAppLogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _orders = orders;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
        => SendBestEffortAsync(
            order,
            OrderNotificationKind.OrderPlaced,
            $"eShopOnWeb: your order #{order.Id} has been placed. Thank you!",
            sendAt: null,
            cancellationToken);

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await SendBestEffortAsync(
            order,
            OrderNotificationKind.OrderDispatched,
            $"eShopOnWeb: your order #{order.Id} is on its way.",
            sendAt: null,
            cancellationToken);

        await SendBestEffortAsync(
            order,
            OrderNotificationKind.DeliveryFollowUp,
            $"eShopOnWeb: how did delivery of order #{order.Id} go?",
            sendAt: DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay),
            cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        await SendBestEffortAsync(
            order,
            OrderNotificationKind.OrderCancelled,
            $"eShopOnWeb: your order #{order.Id} has been cancelled.",
            sendAt: null,
            cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(
        int orderId,
        string buyerId,
        bool isAdministrator,
        CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return Array.Empty<OrderNotification>();
        }

        if (!isAdministrator && !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            return Array.Empty<OrderNotification>();
        }

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderIdSpec(orderId), cancellationToken);
        await RefreshFromProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notifications.ListAsync(new OrderNotificationsByBuyerSpec(buyerId), cancellationToken);
        await RefreshFromProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        var existingResend = await _notifications.FirstOrDefaultAsync(
            new OrderNotificationByResendIdempotencySpec(notificationId, idempotencyKey),
            cancellationToken);
        if (existingResend is not null)
        {
            await RefreshFromProviderAsync(new[] { existingResend }, cancellationToken);
            return existingResend;
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Notification {notificationId} was not found.");

        if (original.ContentRedacted || string.IsNullOrWhiteSpace(original.Body))
        {
            throw new InvalidOperationException("The message content has been disposed of and cannot be resent.");
        }

        if (original.ProviderStatus == "delivered")
        {
            throw new InvalidOperationException("A message that already reached the shopper cannot be resent.");
        }

        var contactStillOnFile = original.ContactNumberId is int contactId
            && await _contactNumbers.FirstOrDefaultAsync(
                new ContactNumberByBuyerAndIdSpec(original.BuyerId, contactId),
                cancellationToken) is not null;
        if (!contactStillOnFile)
        {
            throw new InvalidOperationException("The destination number is no longer on file for this shopper.");
        }

        var resend = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            OrderNotificationKind.Resend,
            original.Body!,
            original.DestinationCanonicalNumber,
            original.ContactNumberId);
        resend.AttachResendMetadata(original.Id, idempotencyKey);
        resend = await _notifications.AddAsync(resend, cancellationToken);

        await DispatchToProviderAsync(resend, sendAt: null, cancellationToken);
        return resend;
    }

    public async Task<OrderNotification> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Notification {notificationId} was not found.");

        var providerSid = notification.ProviderMessageSid;
        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);

        if (!string.IsNullOrWhiteSpace(providerSid))
        {
            try
            {
                var redacted = await _smsGateway.RedactBodyAsync(providerSid, cancellationToken);
                notification.SyncFromProvider(redacted.Status, redacted.ErrorCode, redacted.ErrorMessage);
                notification.MarkContentRedacted();
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (SmsGatewayException ex) when (ex.StatusCode == 404)
            {
                _logger.LogInformation(
                    "Provider message for notification {NotificationId} is already gone; treating content as disposed.",
                    notification.Id);
            }
        }

        var persisted = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Notification {notificationId} was not found after update.");
        return persisted;
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The 'to' timestamp must be on or after 'from'.");
        }

        var providerMessages = await _smsGateway.ListSentByConfiguredNumberAsync(from, to, cancellationToken);
        var providerSids = providerMessages
            .Where(m => !string.IsNullOrWhiteSpace(m.Sid))
            .Select(m => m.Sid!)
            .Distinct()
            .ToList();

        var localBySid = providerSids.Count == 0
            ? new List<OrderNotification>()
            : await _notifications.ListAsync(new OrderNotificationsByProviderSidsSpec(providerSids), cancellationToken);
        var localInRange = await _notifications.ListAsync(new OrderNotificationsInRangeSpec(from, to), cancellationToken);

        var localsBySid = localBySid
            .Concat(localInRange)
            .Where(n => !string.IsNullOrWhiteSpace(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<NotificationReconciliationItem>();
        var providerOnly = new List<NotificationReconciliationItem>();

        foreach (var provider in providerMessages)
        {
            if (string.IsNullOrWhiteSpace(provider.Sid))
            {
                continue;
            }

            if (localsBySid.TryGetValue(provider.Sid, out var local))
            {
                matched.Add(ToReconciliationItem(local, provider));
            }
            else
            {
                providerOnly.Add(new NotificationReconciliationItem
                {
                    ProviderMessageSid = provider.Sid,
                    ProviderStatus = provider.Status,
                    DateSent = provider.DateSent,
                    DateCreated = provider.DateCreated
                });
            }
        }

        var matchedSids = new HashSet<string>(matched.Where(m => m.ProviderMessageSid is not null).Select(m => m.ProviderMessageSid!));
        var localOnly = localInRange
            .Where(n => string.IsNullOrWhiteSpace(n.ProviderMessageSid) || !matchedSids.Contains(n.ProviderMessageSid!))
            .Select(n => ToReconciliationItem(n, provider: null))
            .ToList();

        return new NotificationReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = _smsGateway.ConfiguredFromNumber,
            Matched = matched,
            ProviderOnly = providerOnly,
            LocalOnly = localOnly
        };
    }

    private static NotificationReconciliationItem ToReconciliationItem(OrderNotification local, SmsMessageSnapshot? provider)
        => new()
        {
            NotificationId = local.Id,
            ProviderMessageSid = provider?.Sid ?? local.ProviderMessageSid,
            ProviderStatus = provider?.Status ?? local.ProviderStatus,
            LocalStatus = local.ProviderStatus,
            OrderId = local.OrderId,
            Kind = local.Kind.ToString(),
            DateSent = provider?.DateSent,
            DateCreated = provider?.DateCreated
        };

    private async Task SendBestEffortAsync(
        Order order,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var contact = (await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpec(order.BuyerId), cancellationToken))
                .FirstOrDefault();
            if (contact is null)
            {
                _logger.LogInformation("Skipping {Kind} notification for order {OrderId}; shopper has no number on file.", kind, order.Id);
                return;
            }

            var notification = new OrderNotification(
                order.Id,
                order.BuyerId,
                kind,
                body,
                contact.CanonicalPhoneNumber,
                contact.Id);
            if (sendAt.HasValue)
            {
                notification.MarkScheduled(sendAt.Value);
            }

            notification = await _notifications.AddAsync(notification, cancellationToken);
            await DispatchToProviderAsync(notification, sendAt, cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogWarning("Failed to send {Kind} notification for order {OrderId}. The order operation was not rolled back.", kind, order.Id);
        }
    }

    private async Task DispatchToProviderAsync(OrderNotification notification, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _smsGateway.SendAsync(new SmsSendRequest
            {
                To = notification.DestinationCanonicalNumber,
                Body = notification.Body ?? string.Empty,
                SendAt = sendAt
            }, cancellationToken);

            if (string.IsNullOrWhiteSpace(snapshot.Sid))
            {
                notification.RecordProviderFailure(snapshot.ErrorCode, snapshot.ErrorMessage ?? "The provider did not return a message identifier.");
            }
            else
            {
                notification.RecordProviderAcceptance(snapshot.Sid, snapshot.Status);
            }
        }
        catch (SmsGatewayException ex)
        {
            notification.RecordProviderFailure(ex.ProviderCode, ex.Message);
            _logger.LogWarning("Provider rejected notification {NotificationId} for order {OrderId}.", notification.Id, notification.OrderId);
        }
        catch (Exception)
        {
            notification.RecordProviderFailure(null, "The provider call failed.");
            _logger.LogWarning("Provider call failed for notification {NotificationId} for order {OrderId}.", notification.Id, notification.OrderId);
        }

        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var pending = await _notifications.ListAsync(new ScheduledFollowUpsByOrderSpec(orderId), cancellationToken);
        foreach (var notification in pending)
        {
            if (string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var cancelled = await _smsGateway.CancelAsync(notification.ProviderMessageSid, cancellationToken);
                notification.SyncFromProvider(cancelled.Status, cancelled.ErrorCode, cancelled.ErrorMessage);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception)
            {
                _logger.LogWarning(
                    "Failed to cancel scheduled follow-up {NotificationId} for order {OrderId}.",
                    notification.Id,
                    orderId);
            }
        }
    }

    private async Task RefreshFromProviderAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrWhiteSpace(notification.ProviderMessageSid) || notification.IsTerminalStatus())
            {
                continue;
            }

            try
            {
                var snapshot = await _smsGateway.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                if (snapshot is null)
                {
                    continue;
                }

                notification.SyncFromProvider(snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage);
                if (notification.ContentRedacted)
                {
                    notification.MarkContentRedacted();
                }

                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception)
            {
                _logger.LogWarning("Failed to refresh provider status for notification {NotificationId}.", notification.Id);
            }
        }
    }
}
