using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<NotificationResendRecord> _resendRecords;
    private readonly ISmsGateway _smsGateway;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notifications,
        IRepository<ContactNumber> contactNumbers,
        IRepository<NotificationResendRecord> resendRecords,
        ISmsGateway smsGateway,
        IUnitOfWork unitOfWork,
        IAppLogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _resendRecords = resendRecords;
        _smsGateway = smsGateway;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public Task TryNotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        return TryNotifyAsync(
            order,
            OrderNotificationKind.OrderPlaced,
            $"Your eShopOnWeb order #{order.Id} has been placed. Thank you!",
            sendAt: null,
            cancellationToken);
    }

    public async Task TryNotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await TryNotifyAsync(
            order,
            OrderNotificationKind.OrderDispatched,
            $"Your eShopOnWeb order #{order.Id} is on its way.",
            sendAt: null,
            cancellationToken);

        await TryNotifyAsync(
            order,
            OrderNotificationKind.DeliveryFollowUp,
            $"How did the delivery of eShopOnWeb order #{order.Id} go?",
            sendAt: DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay),
            cancellationToken);
    }

    public async Task TryNotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        await TryNotifyAsync(
            order,
            OrderNotificationKind.OrderCancelled,
            $"Your eShopOnWeb order #{order.Id} has been cancelled.",
            sendAt: null,
            cancellationToken);
    }

    public Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken = default)
        => CancelPendingFollowUpsInternalAsync(orderId, cancellationToken);

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notifications.ListAsync(
            new OrderNotificationsByOrderSpecification(orderId),
            cancellationToken);
        await RefreshProviderStatusAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task RefreshProviderStatusAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _smsGateway.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                if (snapshot == null)
                {
                    continue;
                }

                notification.ApplyProviderSnapshot(snapshot.Status, snapshot.ErrorCode);
                if (notification.ContentRedacted ||
                    (string.IsNullOrEmpty(snapshot.Body) && !string.IsNullOrEmpty(notification.Body)))
                {
                    notification.RedactContent();
                }
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to refresh provider status for notification {NotificationId}: {Message}", notification.Id, ex.Message);
            }
        }
    }

    public async Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var existing = await _resendRecords.FirstOrDefaultAsync(
            new NotificationResendByKeySpecification(notificationId, idempotencyKey),
            cancellationToken);
        if (existing != null)
        {
            return await _notifications.GetByIdAsync(existing.ResultNotificationId, cancellationToken);
        }

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (source == null)
        {
            return null;
        }

        if (source.ContentRedacted || string.IsNullOrEmpty(source.Body))
        {
            throw new InvalidOperationException("The original message content has been disposed of and cannot be re-sent.");
        }

        var stillRegistered = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberStillRegisteredSpecification(source.BuyerId, source.DestinationE164),
            cancellationToken);
        if (stillRegistered == null)
        {
            throw new InvalidOperationException("The destination is no longer on file; nothing will be sent to it.");
        }

        if (!string.IsNullOrEmpty(source.ProviderMessageSid))
        {
            try
            {
                var snapshot = await _smsGateway.FetchAsync(source.ProviderMessageSid, cancellationToken);
                if (snapshot != null)
                {
                    source.ApplyProviderSnapshot(snapshot.Status, snapshot.ErrorCode);
                    await _notifications.UpdateAsync(source, cancellationToken);
                    if (string.Equals(snapshot.Status, "delivered", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("The original message already reached the shopper.");
                    }
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not refresh original notification {NotificationId} before resend: {Message}", source.Id, ex.Message);
            }
        }

        var resent = new OrderNotification(
            source.OrderId,
            source.BuyerId,
            stillRegistered.Id,
            source.DestinationE164,
            source.Kind,
            source.Body);
        resent.MarkResendOf(source.Id);
        await _notifications.AddAsync(resent, cancellationToken);

        var record = new NotificationResendRecord(source.Id, idempotencyKey, resent.Id);
        await _resendRecords.AddAsync(record, cancellationToken);

        await DispatchToProviderAsync(resent, sendAt: null, cancellationToken);

        return resent;
    }

    public async Task<OrderNotification?> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification == null)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            try
            {
                var snapshot = await _smsGateway.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
                if (snapshot != null)
                {
                    notification.ApplyProviderSnapshot(snapshot.Status, snapshot.ErrorCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Provider redaction failed for notification {NotificationId}: {Message}", notification.Id, ex.Message);
                throw;
            }
        }

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return notification;
    }

    private async Task TryNotifyAsync(
        Order order,
        string kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var contacts = await _contactNumbers.ListAsync(
                new ContactNumbersByBuyerSpecification(order.BuyerId),
                cancellationToken);
            if (contacts.Count == 0)
            {
                _logger.LogInformation("No contact number on file for order {OrderId}; skipping {Kind} notification", order.Id, kind);
                return;
            }

            foreach (var contact in contacts)
            {
                var notification = new OrderNotification(
                    order.Id,
                    order.BuyerId,
                    contact.Id,
                    contact.PhoneNumberE164,
                    kind,
                    body);
                await _notifications.AddAsync(notification, cancellationToken);
                await DispatchToProviderAsync(notification, sendAt, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Notification {Kind} for order {OrderId} failed and will not fail the order operation: {Message}", kind, order.Id, ex.Message);
        }
    }

    private async Task DispatchToProviderAsync(
        OrderNotification notification,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _smsGateway.SendAsync(
                new SmsSendRequest
                {
                    ToE164 = notification.DestinationE164,
                    Body = notification.Body ?? string.Empty,
                    SendAt = sendAt
                },
                cancellationToken);

            if (result.Accepted && result.Message?.Sid != null)
            {
                notification.RecordProviderAcceptance(result.Message.Sid, result.Message.Status, sendAt);
            }
            else
            {
                notification.RecordProviderFailure(result.FailureStatus ?? "failed", result.ErrorCode);
            }

            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Provider send failed for notification {NotificationId}: {Message}", notification.Id, ex.Message);
            notification.RecordProviderFailure("failed", null);
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
    }

    private async Task CancelPendingFollowUpsInternalAsync(int orderId, CancellationToken cancellationToken)
    {
        var pending = await _notifications.ListAsync(
            new PendingFollowUpNotificationsSpecification(orderId),
            cancellationToken);

        foreach (var followUp in pending)
        {
            try
            {
                if (string.IsNullOrEmpty(followUp.ProviderMessageSid))
                {
                    continue;
                }

                var snapshot = await _smsGateway.CancelAsync(followUp.ProviderMessageSid, cancellationToken);
                if (snapshot != null)
                {
                    followUp.ApplyProviderSnapshot(snapshot.Status, snapshot.ErrorCode);
                    await _notifications.UpdateAsync(followUp, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to cancel follow-up notification {NotificationId}: {Message}", followUp.Id, ex.Message);
            }
        }
    }
}
