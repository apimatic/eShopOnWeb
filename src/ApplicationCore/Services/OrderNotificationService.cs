using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Extensions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly IRepository<NotificationResendRecord> _resendRecords;
    private readonly ITwilioMessagingClient _messaging;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notifications,
        IRepository<ShopperContactNumber> contactNumbers,
        IRepository<NotificationResendRecord> resendRecords,
        ITwilioMessagingClient messaging,
        IAppLogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _resendRecords = resendRecords;
        _messaging = messaging;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
        => SendToShopperSafelyAsync(
            order,
            NotificationKind.OrderPlaced,
            BuildBody(NotificationKind.OrderPlaced, order.Id),
            scheduledFor: null,
            cancellationToken);

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await SendToShopperSafelyAsync(
            order,
            NotificationKind.OrderDispatched,
            BuildBody(NotificationKind.OrderDispatched, order.Id),
            scheduledFor: null,
            cancellationToken);

        await SendToShopperSafelyAsync(
            order,
            NotificationKind.DeliveryFollowUp,
            BuildBody(NotificationKind.DeliveryFollowUp, order.Id),
            DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay),
            cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        await SendToShopperSafelyAsync(
            order,
            NotificationKind.OrderCancelled,
            BuildBody(NotificationKind.OrderCancelled, order.Id),
            scheduledFor: null,
            cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notifications.ListAsync(new NotificationsByOrderIdSpecification(orderId), cancellationToken);
        await RefreshFromProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notifications.ListAsync(new NotificationsByBuyerSpecification(buyerId), cancellationToken);
        await RefreshFromProviderAsync(notifications, cancellationToken);
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

        var existing = await _resendRecords.FirstOrDefaultAsync(
            new ResendRecordByKeySpecification(original.Id, idempotencyKey.Trim()),
            cancellationToken);
        if (existing is not null)
        {
            var previous = await _notifications.GetByIdAsync(existing.ResultingNotificationId, cancellationToken);
            if (previous is not null)
            {
                return previous;
            }
        }

        var stillRegistered = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpecification(original.BuyerId, original.DestinationNumber),
            cancellationToken);
        if (stillRegistered is null)
        {
            throw new InvalidOperationException("The destination contact number is no longer on file, so the message was not sent.");
        }

        var body = original.ContentRedacted || string.IsNullOrWhiteSpace(original.Body)
            ? BuildBody(original.Kind, original.OrderId)
            : original.Body;

        var resent = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            stillRegistered.CanonicalNumber,
            stillRegistered.Id,
            original.Kind,
            body,
            scheduledFor: null,
            resentFromNotificationId: original.Id);

        await DeliverAsync(resent, cancellationToken);
        await _notifications.AddAsync(resent, cancellationToken);
        await _resendRecords.AddAsync(new NotificationResendRecord(original.Id, idempotencyKey.Trim(), resent.Id), cancellationToken);
        return resent;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException("Notification was not found.");

        if (!string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            await _messaging.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _messaging.ListSentFromConfiguredNumberAsync(from, to, cancellationToken);
        var localNotifications = await _notifications.ListAsync(new NotificationsCreatedInRangeSpecification(from, to), cancellationToken);

        var localBySid = localNotifications
            .Where(n => !string.IsNullOrWhiteSpace(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrWhiteSpace(m.Sid))
            .GroupBy(m => m.Sid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var applicationOnly = new List<ReconciliationEntry>();

        foreach (var provider in providerMessages.Where(m => !string.IsNullOrWhiteSpace(m.Sid)))
        {
            if (localBySid.TryGetValue(provider.Sid!, out var local))
            {
                matched.Add(new ReconciliationEntry(
                    provider.Sid,
                    local.Id,
                    provider.Status,
                    local.DeliveryStatus,
                    provider.DateSent ?? provider.DateCreated,
                    "matched"));
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry(
                    provider.Sid,
                    null,
                    provider.Status,
                    null,
                    provider.DateSent ?? provider.DateCreated,
                    "provider_only"));
            }
        }

        foreach (var local in localNotifications)
        {
            if (string.IsNullOrWhiteSpace(local.ProviderMessageSid) || !providerBySid.ContainsKey(local.ProviderMessageSid))
            {
                applicationOnly.Add(new ReconciliationEntry(
                    local.ProviderMessageSid,
                    local.Id,
                    null,
                    local.DeliveryStatus,
                    local.CreatedAt,
                    "application_only"));
            }
        }

        return new ReconciliationReport(from, to, _messaging.ConfiguredFromNumber, matched, providerOnly, applicationOnly);
    }

    private async Task SendToShopperSafelyAsync(
        Order order,
        NotificationKind kind,
        string body,
        DateTimeOffset? scheduledFor,
        CancellationToken cancellationToken)
    {
        try
        {
            var destinations = await _contactNumbers.ListAsync(
                new ContactNumbersByBuyerSpecification(order.BuyerId),
                cancellationToken);

            if (destinations.Count == 0)
            {
                _logger.LogInformation("Skipping {Kind} notification for order {OrderId}; shopper has no contact number on file.", kind, order.Id);
                return;
            }

            foreach (var destination in destinations)
            {
                var notification = new OrderNotification(
                    order.Id,
                    order.BuyerId,
                    destination.CanonicalNumber,
                    destination.Id,
                    kind,
                    body,
                    scheduledFor);

                try
                {
                    await DeliverAsync(notification, cancellationToken);
                }
                catch (Exception ex)
                {
                    notification.MarkSendFailed(null, PhoneNumberSanitizer.Sanitize(ex.Message));
                    _logger.LogWarning(
                        "Failed to send {Kind} notification for order {OrderId}: {Message}",
                        kind,
                        order.Id,
                        PhoneNumberSanitizer.Sanitize(ex.Message));
                }

                await _notifications.AddAsync(notification, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Order {OrderId} {Kind} notification flow failed and was swallowed: {Message}",
                order.Id,
                kind,
                PhoneNumberSanitizer.Sanitize(ex.Message));
        }
    }

    private async Task DeliverAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        TwilioMessageResult result;
        if (notification.ScheduledFor.HasValue)
        {
            result = await _messaging.ScheduleAsync(
                notification.DestinationNumber,
                notification.Body ?? string.Empty,
                notification.ScheduledFor.Value,
                cancellationToken);
        }
        else
        {
            result = await _messaging.SendAsync(
                notification.DestinationNumber,
                notification.Body ?? string.Empty,
                cancellationToken);
        }

        notification.RecordProviderResult(
            result.Sid,
            result.Status,
            result.ErrorCode,
            PhoneNumberSanitizer.Sanitize(result.ErrorMessage));
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        try
        {
            var followUps = await _notifications.ListAsync(
                new ScheduledFollowUpsByOrderSpecification(orderId),
                cancellationToken);

            foreach (var followUp in followUps)
            {
                if (string.IsNullOrWhiteSpace(followUp.ProviderMessageSid))
                {
                    continue;
                }

                try
                {
                    var current = await _messaging.FetchAsync(followUp.ProviderMessageSid, cancellationToken);
                    if (current is not null)
                    {
                        followUp.RecordProviderResult(
                            current.Sid,
                            current.Status,
                            current.ErrorCode,
                            PhoneNumberSanitizer.Sanitize(current.ErrorMessage));
                    }

                    if (string.Equals(followUp.DeliveryStatus, "scheduled", StringComparison.OrdinalIgnoreCase))
                    {
                        var cancelled = await _messaging.CancelScheduledAsync(followUp.ProviderMessageSid, cancellationToken);
                        followUp.RecordProviderResult(
                            cancelled.Sid,
                            cancelled.Status,
                            cancelled.ErrorCode,
                            PhoneNumberSanitizer.Sanitize(cancelled.ErrorMessage));
                    }

                    await _notifications.UpdateAsync(followUp, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        "Failed to cancel follow-up notification {NotificationId} for order {OrderId}: {Message}",
                        followUp.Id,
                        orderId,
                        PhoneNumberSanitizer.Sanitize(ex.Message));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Failed to load follow-up notifications for order {OrderId}: {Message}",
                orderId,
                PhoneNumberSanitizer.Sanitize(ex.Message));
        }
    }

    private async Task RefreshFromProviderAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var current = await _messaging.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                if (current is null)
                {
                    continue;
                }

                notification.RecordProviderResult(
                    current.Sid,
                    current.Status,
                    current.ErrorCode,
                    PhoneNumberSanitizer.Sanitize(current.ErrorMessage));

                if (notification.ContentRedacted || string.IsNullOrEmpty(current.Body))
                {
                    notification.MarkContentRedacted();
                }

                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Failed to refresh notification {NotificationId} from provider: {Message}",
                    notification.Id,
                    PhoneNumberSanitizer.Sanitize(ex.Message));
            }
        }
    }

    private static string BuildBody(NotificationKind kind, int orderId) => kind switch
    {
        NotificationKind.OrderPlaced => $"Your eShopOnWeb order #{orderId} has been placed. We'll update you when it ships.",
        NotificationKind.OrderDispatched => $"Your eShopOnWeb order #{orderId} is on its way.",
        NotificationKind.DeliveryFollowUp => $"How did the delivery of your eShopOnWeb order #{orderId} go?",
        NotificationKind.OrderCancelled => $"Your eShopOnWeb order #{orderId} has been cancelled.",
        _ => $"An update is available for your eShopOnWeb order #{orderId}."
    };
}
