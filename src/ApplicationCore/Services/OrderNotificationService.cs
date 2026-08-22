using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<NotificationResendRecord> _resendRecords;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ITwilioMessagingClient _messagingClient;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notifications,
        IRepository<NotificationResendRecord> resendRecords,
        IRepository<ContactNumber> contactNumbers,
        ITwilioMessagingClient messagingClient,
        IAppLogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _resendRecords = resendRecords;
        _contactNumbers = contactNumbers;
        _messagingClient = messagingClient;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(order, nameof(order));
        var body = $"Your eShop order #{order.Id} has been placed.";
        return SendBestEffortAsync(order, NotificationKind.OrderPlaced, body, scheduleAt: null, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(order, nameof(order));
        var dispatchedBody = $"Your eShop order #{order.Id} is on its way.";
        await SendBestEffortAsync(order, NotificationKind.OrderDispatched, dispatchedBody, scheduleAt: null, cancellationToken);

        var followUpBody = $"How did the delivery of eShop order #{order.Id} go?";
        await SendBestEffortAsync(order, NotificationKind.DeliveryFollowUp, followUpBody, DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay), cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(order, nameof(order));
        await CancelScheduledFollowUpsAsync(order.Id, cancellationToken);

        var body = $"Your eShop order #{order.Id} has been cancelled.";
        await SendBestEffortAsync(order, NotificationKind.OrderCancelled, body, scheduleAt: null, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> GetForOrderAsync(int orderId, bool refreshFromProvider, CancellationToken cancellationToken = default)
    {
        var notifications = await _notifications.ListAsync(new NotificationsByOrderIdSpec(orderId), cancellationToken);
        if (refreshFromProvider)
        {
            await RefreshAsync(notifications, cancellationToken);
        }

        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>> GetForOrdersAsync(IReadOnlyCollection<int> orderIds, bool refreshFromProvider, CancellationToken cancellationToken = default)
    {
        if (orderIds.Count == 0)
        {
            return Array.Empty<OrderNotification>();
        }

        var notifications = await _notifications.ListAsync(new NotificationsByOrderIdsSpec(orderIds), cancellationToken);
        if (refreshFromProvider)
        {
            await RefreshAsync(notifications, cancellationToken);
        }

        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var existingKey = await _resendRecords.FirstOrDefaultAsync(
            new ResendRecordByKeySpec(notificationId, idempotencyKey),
            cancellationToken);

        if (existingKey is not null)
        {
            var previous = await _notifications.GetByIdAsync(existingKey.ResultNotificationId, cancellationToken);
            if (previous is not null)
            {
                return previous;
            }
        }

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotificationNotFoundException(notificationId);

        if (!string.IsNullOrEmpty(source.ProviderMessageSid) && !source.HasTerminalProviderStatus)
        {
            await RefreshAsync(new[] { source }, cancellationToken);
        }

        if (!source.DidNotReachShopper)
        {
            throw new NotificationCannotBeResentException("This message already reached the shopper or is still in flight.");
        }

        var destination = await ResolveActiveDestinationAsync(source, cancellationToken);
        if (destination is null)
        {
            throw new NotificationCannotBeResentException("The original destination is no longer on file for this shopper.");
        }

        var body = source.ContentRedacted || string.IsNullOrWhiteSpace(source.Body)
            ? $"Update about your eShop order #{source.OrderId}."
            : source.Body;

        var resent = new OrderNotification(
            source.OrderId,
            source.BuyerId,
            NotificationKind.Resend,
            body!,
            destination.Id,
            destination.PhoneNumber,
            source.Id);

        resent = await _notifications.AddAsync(resent, cancellationToken);
        await DeliverAsync(resent, scheduleAt: null, cancellationToken);

        var record = new NotificationResendRecord(source.Id, idempotencyKey, resent.Id);
        await _resendRecords.AddAsync(record, cancellationToken);

        return resent;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotificationNotFoundException(notificationId);

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            var result = await _messagingClient.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
            if (!result.Succeeded)
            {
                _logger.LogWarning(
                    "Provider content redaction failed for notification {NotificationId} with error {ErrorCode}.",
                    notification.Id,
                    result.ErrorCode ?? "none");
                throw new InvalidOperationException(
                    $"The provider could not dispose of the message content ({result.ErrorCode ?? "unknown"}).");
            }

            notification.ApplyProviderSnapshot(result.Status, result.ErrorCode, result.ErrorMessage, body: null);
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _messagingClient.ListFromConfiguredSenderAsync(from, to, cancellationToken);
        var localNotifications = await _notifications.ListAsync(new NotificationsCreatedInRangeSpec(from, to), cancellationToken);

        var localsBySid = localNotifications
            .Where(notification => !string.IsNullOrEmpty(notification.ProviderMessageSid))
            .GroupBy(notification => notification.ProviderMessageSid!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var matched = new List<ReconciledNotification>();
        var providerOnly = new List<ProviderOnlyMessage>();
        var seenSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in providerMessages)
        {
            if (string.IsNullOrEmpty(provider.Sid))
            {
                continue;
            }

            seenSids.Add(provider.Sid);
            if (localsBySid.TryGetValue(provider.Sid, out var local))
            {
                matched.Add(ToReconciled(local, provider.Status));
            }
            else
            {
                providerOnly.Add(new ProviderOnlyMessage
                {
                    ProviderMessageSid = provider.Sid,
                    ProviderStatus = provider.Status,
                    DateSent = provider.DateSent,
                    DateCreated = provider.DateCreated
                });
            }
        }

        var eshopOnly = localNotifications
            .Where(notification =>
                string.IsNullOrEmpty(notification.ProviderMessageSid) ||
                !seenSids.Contains(notification.ProviderMessageSid))
            .Select(notification => ToReconciled(notification, providerStatus: null))
            .ToList();

        return new NotificationReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = _messagingClient.ConfiguredFromNumber,
            Matched = matched,
            ProviderOnly = providerOnly,
            EshopOnly = eshopOnly
        };
    }

    private static ReconciledNotification ToReconciled(OrderNotification notification, string? providerStatus) =>
        new()
        {
            NotificationId = notification.Id,
            OrderId = notification.OrderId,
            ProviderMessageSid = notification.ProviderMessageSid,
            EshopStatus = notification.ProviderStatus,
            ProviderStatus = providerStatus,
            Kind = notification.Kind.ToString()
        };

    private async Task SendBestEffortAsync(
        Order order,
        NotificationKind kind,
        string body,
        DateTimeOffset? scheduleAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var destination = await GetLatestContactAsync(order.BuyerId, cancellationToken);
            if (destination is null)
            {
                _logger.LogInformation("Skipping {Kind} notification for order {OrderId}; shopper has no number on file.", kind, order.Id);
                return;
            }

            var notification = new OrderNotification(order.Id, order.BuyerId, kind, body, destination.Id, destination.PhoneNumber);
            if (scheduleAt.HasValue)
            {
                notification.SetScheduledSendAt(scheduleAt.Value);
            }

            notification = await _notifications.AddAsync(notification, cancellationToken);
            await DeliverAsync(notification, scheduleAt, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Best-effort {Kind} notification for order {OrderId} failed: {ExceptionType}.", kind, order.Id, ex.GetType().Name);
        }
    }

    private async Task DeliverAsync(OrderNotification notification, DateTimeOffset? scheduleAt, CancellationToken cancellationToken)
    {
        try
        {
            ProviderMessageSnapshot result;
            if (scheduleAt.HasValue)
            {
                result = await _messagingClient.ScheduleAsync(notification.DestinationPhoneNumber, notification.Body ?? string.Empty, scheduleAt.Value, cancellationToken);
            }
            else
            {
                result = await _messagingClient.SendAsync(notification.DestinationPhoneNumber, notification.Body ?? string.Empty, cancellationToken);
            }

            if (result.Succeeded && !string.IsNullOrEmpty(result.Sid))
            {
                notification.RecordProviderAccepted(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage);
            }
            else
            {
                notification.RecordProviderFailure(result.ErrorCode, result.ErrorMessage);
                _logger.LogWarning(
                    "Provider rejected {Kind} notification {NotificationId} for order {OrderId} with error {ErrorCode}.",
                    notification.Kind,
                    notification.Id,
                    notification.OrderId,
                    result.ErrorCode ?? "none");
            }
        }
        catch (Exception ex)
        {
            notification.RecordProviderFailure("local_send_failure", ex.GetType().Name);
            _logger.LogWarning(
                "Provider call failed for notification {NotificationId} for order {OrderId}: {ExceptionType}.",
                notification.Id,
                notification.OrderId,
                ex.GetType().Name);
        }

        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    private async Task CancelScheduledFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notifications.ListAsync(new ScheduledFollowUpsByOrderIdSpec(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            if (string.IsNullOrEmpty(followUp.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _messagingClient.FetchAsync(followUp.ProviderMessageSid, cancellationToken);
                if (snapshot is not null)
                {
                    followUp.ApplyProviderSnapshot(snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage, snapshot.Body);
                }

                if (followUp.IsScheduledFollowUp ||
                    string.Equals(followUp.ProviderStatus, "scheduled", StringComparison.OrdinalIgnoreCase))
                {
                    var cancelled = await _messagingClient.CancelScheduledAsync(followUp.ProviderMessageSid, cancellationToken);
                    if (cancelled.Succeeded)
                    {
                        followUp.ApplyProviderSnapshot(cancelled.Status, cancelled.ErrorCode, cancelled.ErrorMessage, cancelled.Body);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Could not cancel scheduled follow-up {NotificationId} for order {OrderId}: {ErrorCode}.",
                            followUp.Id,
                            orderId,
                            cancelled.ErrorCode ?? "none");
                    }
                }

                await _notifications.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Failed to cancel scheduled follow-up {NotificationId} for order {OrderId}: {ExceptionType}.",
                    followUp.Id,
                    orderId,
                    ex.GetType().Name);
            }
        }
    }

    private async Task RefreshAsync(IReadOnlyCollection<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _messagingClient.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                if (snapshot is null)
                {
                    continue;
                }

                var status = snapshot.Status ?? notification.ProviderStatus;
                var providerMayHaveDroppedBody =
                    !string.IsNullOrEmpty(status) &&
                    status is not ("queued" or "accepted" or "scheduled" or "sending" or "pending");
                var providerBodyCleared = snapshot.Succeeded &&
                                          string.IsNullOrEmpty(snapshot.Body) &&
                                          providerMayHaveDroppedBody;
                notification.ApplyProviderSnapshot(
                    snapshot.Status,
                    snapshot.ErrorCode,
                    snapshot.ErrorMessage,
                    providerBodyCleared ? null : snapshot.Body);

                if (providerBodyCleared || notification.ContentRedacted)
                {
                    notification.MarkContentRedacted();
                }

                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Failed to refresh provider status for notification {NotificationId}: {ExceptionType}.",
                    notification.Id,
                    ex.GetType().Name);
            }
        }
    }

    private async Task<ContactNumber?> GetLatestContactAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerIdSpec(buyerId), cancellationToken);
        return numbers.FirstOrDefault();
    }

    private async Task<ContactNumber?> ResolveActiveDestinationAsync(OrderNotification source, CancellationToken cancellationToken)
    {
        if (source.ContactNumberId.HasValue)
        {
            var byId = await _contactNumbers.FirstOrDefaultAsync(
                new ContactNumberByIdAndBuyerSpec(source.ContactNumberId.Value, source.BuyerId),
                cancellationToken);
            if (byId is not null)
            {
                return byId;
            }
        }

        return await _contactNumbers.FirstOrDefaultAsync(
            new ActiveContactNumberForDestinationSpec(source.BuyerId, source.DestinationPhoneNumber),
            cancellationToken);
    }
}
