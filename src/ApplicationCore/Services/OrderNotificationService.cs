using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<ShopperContactNumber> _contactRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<NotificationResendRecord> _resendRepository;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<ShopperContactNumber> contactRepository,
        IRepository<OrderNotification> notificationRepository,
        IRepository<NotificationResendRecord> resendRepository,
        ISmsGateway smsGateway,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactRepository = contactRepository;
        _notificationRepository = notificationRepository;
        _resendRepository = resendRepository;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        return NotifyAsync(
            order,
            OrderNotificationKind.OrderPlaced,
            $"eShopOnWeb: Your order #{order.Id} has been placed. Total: {order.Total():0.00}.",
            sendAt: null,
            cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await NotifyAsync(
            order,
            OrderNotificationKind.OrderDispatched,
            $"eShopOnWeb: Your order #{order.Id} is on its way.",
            sendAt: null,
            cancellationToken);

        await NotifyAsync(
            order,
            OrderNotificationKind.DeliveryFollowUp,
            $"eShopOnWeb: How did the delivery of order #{order.Id} go?",
            sendAt: DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay),
            cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        await CancelScheduledFollowUpsAsync(
            await _notificationRepository.ListAsync(new ScheduledFollowUpsByOrderSpecification(order.Id), cancellationToken),
            cancellationToken);

        await NotifyAsync(
            order,
            OrderNotificationKind.OrderCancelled,
            $"eShopOnWeb: Your order #{order.Id} has been cancelled.",
            sendAt: null,
            cancellationToken);
    }

    public async Task CancelScheduledForContactAsync(int contactNumberId, CancellationToken cancellationToken = default)
    {
        var scheduled = await _notificationRepository.ListAsync(
            new ScheduledFollowUpsByContactSpecification(contactNumberId), cancellationToken);
        await CancelScheduledFollowUpsAsync(scheduled, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, bool refreshFromProvider, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.ListAsync(
            new OrderNotificationsByOrderSpecification(orderId), cancellationToken);

        if (refreshFromProvider)
        {
            await RefreshFromProviderAsync(notifications, cancellationToken);
        }

        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new InvalidOrderStateException("An idempotency key is required.");
        }

        var existing = await _resendRepository.FirstOrDefaultAsync(
            new NotificationResendByKeySpecification(notificationId, idempotencyKey.Trim()), cancellationToken);
        if (existing is not null)
        {
            var previous = await _notificationRepository.GetByIdAsync(existing.ResultNotificationId, cancellationToken);
            if (previous is not null)
            {
                return previous;
            }
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            throw new NotificationNotFoundException(notificationId);
        }

        if (original.ContentRedacted)
        {
            throw new InvalidOrderStateException("This message's content has been disposed of and cannot be resent.");
        }

        if (!original.WasNotDelivered())
        {
            throw new InvalidOrderStateException("This message cannot be resent because it has not failed delivery.");
        }

        if (string.IsNullOrWhiteSpace(original.Body))
        {
            throw new InvalidOrderStateException("This message has no content to resend.");
        }

        var resent = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            original.ContactNumberId,
            original.DestinationNumber,
            original.Kind,
            original.Body);

        resent = await _notificationRepository.AddAsync(resent, cancellationToken);
        await DeliverAsync(resent, sendAt: null, cancellationToken);

        var record = new NotificationResendRecord(original.Id, idempotencyKey.Trim(), resent.Id);
        try
        {
            await _resendRepository.AddAsync(record, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Stored the resent notification {NotificationId} but could not persist the idempotency record: {Message}", resent.Id, ex.Message);
        }

        return resent;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            throw new NotificationNotFoundException(notificationId);
        }

        if (notification.HasProviderIdentity)
        {
            try
            {
                await _smsGateway.RedactBodyAsync(notification.ProviderMessageSid!, cancellationToken);
            }
            catch (Exception ex)
            {
                // A just-accepted message can 404 on update; confirm the provider body is already gone.
                try
                {
                    var current = await _smsGateway.FetchAsync(notification.ProviderMessageSid!, cancellationToken);
                    if (current.Body is { Length: > 0 })
                    {
                        _logger.LogWarning("Could not redact provider content for notification {NotificationId}: {Message}", notification.Id, ex.Message);
                        throw;
                    }
                }
                catch (Exception fetchEx) when (fetchEx != ex)
                {
                    _logger.LogWarning("Could not redact provider content for notification {NotificationId}: {Message}", notification.Id, ex.Message);
                    throw;
                }
            }
        }

        notification.MarkContentRedacted();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new InvalidOrderStateException("The reconciliation range is invalid: 'to' must be on or after 'from'.");
        }

        var fromNumber = _smsGateway.FromNumber;
        if (string.IsNullOrWhiteSpace(fromNumber))
        {
            throw new InvalidOrderStateException("Twilio:FromNumber is not configured.");
        }

        var providerMessages = await _smsGateway.ListSentFromAsync(fromNumber, from, to, cancellationToken);
        var eshopNotifications = await _notificationRepository.ListAsync(
            new OrderNotificationsInRangeSpecification(from, to), cancellationToken);

        var eshopBySid = eshopNotifications
            .Where(n => n.HasProviderIdentity)
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matched = new List<ReconciliationMatch>();
        var providerOnly = new List<ProviderMessageSnapshot>();
        var matchedSids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var provider in providerMessages)
        {
            if (string.IsNullOrWhiteSpace(provider.Sid))
            {
                continue;
            }

            if (eshopBySid.TryGetValue(provider.Sid, out var local))
            {
                matchedSids.Add(provider.Sid);
                matched.Add(new ReconciliationMatch
                {
                    NotificationId = local.Id,
                    ProviderMessageSid = provider.Sid,
                    EshopStatus = local.ProviderStatus,
                    ProviderStatus = provider.Status
                });
            }
            else
            {
                providerOnly.Add(new ProviderMessageSnapshot
                {
                    Sid = provider.Sid,
                    Status = provider.Status,
                    To = provider.To,
                    From = provider.From,
                    DateSent = provider.DateSent,
                    Body = provider.Body
                });
            }
        }

        var eshopOnly = eshopNotifications
            .Where(n => !n.HasProviderIdentity || !matchedSids.Contains(n.ProviderMessageSid!))
            .ToList();

        return new NotificationReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = fromNumber,
            Matched = matched,
            ProviderOnly = providerOnly,
            EshopOnly = eshopOnly
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
            var destinations = await _contactRepository.ListAsync(
                new ShopperContactNumbersSpecification(order.BuyerId), cancellationToken);

            if (destinations.Count == 0)
            {
                _logger.LogInformation("No contact number on file for the buyer of order {OrderId}; skipping {Kind} notification.", order.Id, kind);
                return;
            }

            foreach (var destination in destinations)
            {
                var notification = new OrderNotification(
                    order.Id,
                    order.BuyerId,
                    destination.Id,
                    destination.CanonicalNumber,
                    kind,
                    body,
                    sendAt);

                notification = await _notificationRepository.AddAsync(notification, cancellationToken);
                await DeliverAsync(notification, sendAt, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Notification {Kind} for order {OrderId} did not complete: {Message}", kind, order.Id, ex.Message);
        }
    }

    private async Task DeliverAsync(OrderNotification notification, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _smsGateway.SendAsync(
                new SmsSendRequest
                {
                    To = notification.DestinationNumber,
                    Body = notification.Body,
                    SendAt = sendAt
                },
                cancellationToken);

            if (string.IsNullOrWhiteSpace(result.Sid))
            {
                notification.RecordLocalSendFailure("The provider did not return a message identifier.");
            }
            else
            {
                notification.RecordProviderAcceptance(result.Sid, result.Status, result.DateSent);
            }

            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Provider send failed for notification {NotificationId} of kind {Kind}: {Message}", notification.Id, notification.Kind, ex.Message);
            notification.RecordLocalSendFailure("The provider did not accept the message.");
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
    }

    private async Task CancelScheduledFollowUpsAsync(IEnumerable<OrderNotification> scheduled, CancellationToken cancellationToken)
    {
        foreach (var notification in scheduled)
        {
            if (!notification.HasProviderIdentity)
            {
                continue;
            }

            var status = notification.ProviderStatus;
            if (status is "canceled" or "sent" or "delivered" or "undelivered" or "failed" or "read")
            {
                continue;
            }

            try
            {
                var result = await _smsGateway.CancelAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.SyncFromProvider(result.Status, result.ErrorCode, null, result.DateSent, result.Body);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not cancel scheduled follow-up {NotificationId}: {Message}", notification.Id, ex.Message);
            }
        }
    }

    private async Task RefreshFromProviderAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        var pending = notifications.Where(n => n.HasProviderIdentity && !n.ContentRedacted).ToList();
        foreach (var notification in pending)
        {
            try
            {
                var result = await _smsGateway.FetchAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.SyncFromProvider(result.Status, result.ErrorCode, null, result.DateSent, result.Body);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not refresh provider status for notification {NotificationId}: {Message}", notification.Id, ex.Message);
            }
        }

        foreach (var notification in notifications.Where(n => n.HasProviderIdentity && n.ContentRedacted))
        {
            try
            {
                var result = await _smsGateway.FetchAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.SyncFromProvider(result.Status, result.ErrorCode, errorMessage: null, result.DateSent, body: null);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not refresh provider status for redacted notification {NotificationId}: {Message}", notification.Id, ex.Message);
            }
        }
    }
}
