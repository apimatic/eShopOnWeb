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
using Microsoft.eShopWeb.ApplicationCore.Extensions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<Order> _orders;
    private readonly ITwilioApiClient _twilio;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notifications,
        IRepository<ContactNumber> contactNumbers,
        IRepository<Order> orders,
        ITwilioApiClient twilio,
        IAppLogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _orders = orders;
        _twilio = twilio;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        return TryNotifyAsync(
            orderId,
            buyerId,
            OrderNotificationKind.OrderPlaced,
            $"Your eShopOnWeb order #{orderId} has been placed.",
            sendAt: null,
            cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        await TryNotifyAsync(
            orderId,
            buyerId,
            OrderNotificationKind.OrderDispatched,
            $"Your eShopOnWeb order #{orderId} is on its way.",
            sendAt: null,
            cancellationToken);

        await TryNotifyAsync(
            orderId,
            buyerId,
            OrderNotificationKind.DeliveryFollowUp,
            $"How did the delivery of eShopOnWeb order #{orderId} go?",
            sendAt: DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay),
            cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        await CancelPendingFollowUpsAsync(orderId, cancellationToken);

        await TryNotifyAsync(
            orderId,
            buyerId,
            OrderNotificationKind.OrderCancelled,
            $"Your eShopOnWeb order #{orderId} has been cancelled.",
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
            throw new EntityNotFoundException("Order not found.");
        }

        if (!isAdministrator && order.BuyerId != buyerId)
        {
            throw new EntityNotFoundException("Order not found.");
        }

        var items = await _notifications.ListAsync(new OrderNotificationsByOrderIdSpecification(orderId), cancellationToken);
        await RefreshProviderStateAsync(items, cancellationToken);
        return items;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var items = await _notifications.ListAsync(new OrderNotificationsByBuyerIdSpecification(buyerId), cancellationToken);
        await RefreshProviderStateAsync(items, cancellationToken);
        return items;
    }

    public async Task RefreshProviderStateAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _twilio.FetchMessageAsync(notification.ProviderMessageSid, cancellationToken);
                ApplySnapshot(notification, snapshot);
                if (notification.ContentRedacted)
                {
                    notification.RedactContent();
                }

                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Failed to refresh provider state for notification {NotificationId}: {Message}",
                    notification.Id,
                    PhoneNumberLogSanitizer.Redact(ex.Message));
            }
        }
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var existingResend = await _notifications.FirstOrDefaultAsync(
            new ResendByIdempotencySpecification(notificationId, idempotencyKey),
            cancellationToken);
        if (existingResend is not null)
        {
            return existingResend;
        }

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (source is null)
        {
            throw new EntityNotFoundException("Notification not found.");
        }

        if (!string.IsNullOrEmpty(source.ProviderMessageSid))
        {
            try
            {
                var snapshot = await _twilio.FetchMessageAsync(source.ProviderMessageSid, cancellationToken);
                ApplySnapshot(source, snapshot);
                await _notifications.UpdateAsync(source, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Failed to refresh notification {NotificationId} before resend: {Message}",
                    source.Id,
                    PhoneNumberLogSanitizer.Redact(ex.Message));
            }
        }

        if (source.HasReachedShopper())
        {
            throw new InvalidOrderStateException("This message already reached the shopper and cannot be resent.");
        }

        var destinationStillOnFile = await DestinationStillRegisteredAsync(source, cancellationToken);
        if (!destinationStillOnFile)
        {
            throw new InvalidOrderStateException("The destination number is no longer on file and cannot be messaged.");
        }

        var body = source.Body;
        if (string.IsNullOrEmpty(body))
        {
            body = $"Update about your eShopOnWeb order #{source.OrderId}.";
        }

        var resend = new OrderNotification(
            source.OrderId,
            source.BuyerId,
            OrderNotificationKind.Resend,
            body,
            source.DestinationNumber,
            source.ContactNumberId);
        resend.AttachResend(source.Id, idempotencyKey);

        await SendAndPersistAsync(resend, sendAt: null, cancellationToken);
        return resend;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            throw new EntityNotFoundException("Notification not found.");
        }

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            try
            {
                await _twilio.RedactMessageBodyAsync(notification.ProviderMessageSid, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Provider content disposal failed for notification {NotificationId}: {Message}",
                    notification.Id,
                    PhoneNumberLogSanitizer.Redact(ex.Message));
                throw;
            }
        }

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new InvalidOrderStateException("The reconciliation 'to' value must be on or after 'from'.");
        }

        var fromNumber = await ResolveFromNumberAsync();
        var providerMessages = await _twilio.ListMessagesFromAsync(fromNumber, from, to, cancellationToken);

        var eshopNotifications = await _notifications.ListAsync(
            new OrderNotificationsWithProviderSidSpecification(from.AddDays(-1), to.AddDays(1)),
            cancellationToken);

        var eshopBySid = eshopNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var providerSids = new HashSet<string>(providerMessages.Select(m => m.Sid), StringComparer.Ordinal);
        var items = new List<NotificationReconciliationItem>();

        foreach (var message in providerMessages)
        {
            eshopBySid.TryGetValue(message.Sid, out var local);
            items.Add(new NotificationReconciliationItem(
                message.Sid,
                message.Status,
                message.DateSent ?? message.DateCreated,
                local?.Id,
                local is null ? "missing-in-eshop" : "matched"));
        }

        foreach (var local in eshopNotifications.Where(n =>
                     !string.IsNullOrEmpty(n.ProviderMessageSid) &&
                     n.CreatedUtc >= from &&
                     n.CreatedUtc <= to &&
                     !providerSids.Contains(n.ProviderMessageSid!)))
        {
            items.Add(new NotificationReconciliationItem(
                local.ProviderMessageSid!,
                local.ProviderStatus,
                local.CreatedUtc,
                local.Id,
                "missing-in-provider"));
        }

        var matched = items.Count(i => i.Match == "matched");
        var missingInEshop = items.Count(i => i.Match == "missing-in-eshop");
        var missingInProvider = items.Count(i => i.Match == "missing-in-provider");

        return new NotificationReconciliationReport(
            from,
            to,
            fromNumber,
            items,
            providerMessages.Count,
            eshopNotifications.Count(n => n.CreatedUtc >= from && n.CreatedUtc <= to && n.ProviderMessageSid != null),
            matched,
            missingInEshop,
            missingInProvider);
    }

    private async Task TryNotifyAsync(
        int orderId,
        string buyerId,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var contact = await GetCurrentContactAsync(buyerId, cancellationToken);
            if (contact is null)
            {
                _logger.LogInformation("Skipping SMS for order {OrderId}; shopper has no number on file.", orderId);
                return;
            }

            var notification = new OrderNotification(orderId, buyerId, kind, body, contact.CanonicalNumber, contact.Id);
            await SendAndPersistAsync(notification, sendAt, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Order {OrderId} notification ({Kind}) could not be sent: {Message}",
                orderId,
                kind,
                PhoneNumberLogSanitizer.Redact(ex.Message));
        }
    }

    private async Task SendAndPersistAsync(OrderNotification notification, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        if (sendAt.HasValue)
        {
            notification.MarkScheduled(sendAt.Value);
        }

        try
        {
            var snapshot = await _twilio.SendMessageAsync(
                new SendSmsRequest(notification.DestinationNumber, notification.Body ?? string.Empty, sendAt),
                cancellationToken);
            ApplySnapshot(notification, snapshot);
        }
        catch (Exception ex)
        {
            notification.MarkSendFailed(PhoneNumberLogSanitizer.Redact(ex.Message));
            _logger.LogWarning(
                "Provider rejected or failed SMS for order {OrderId}: {Message}",
                notification.OrderId,
                PhoneNumberLogSanitizer.Redact(ex.Message));
        }

        if (notification.Id == 0)
        {
            await _notifications.AddAsync(notification, cancellationToken);
        }
        else
        {
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var pending = await _notifications.ListAsync(new PendingFollowUpNotificationsSpecification(orderId), cancellationToken);
        foreach (var followUp in pending)
        {
            try
            {
                var snapshot = await _twilio.CancelScheduledMessageAsync(followUp.ProviderMessageSid!, cancellationToken);
                ApplySnapshot(followUp, snapshot);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Failed to cancel scheduled follow-up {NotificationId} for order {OrderId}: {Message}",
                    followUp.Id,
                    orderId,
                    PhoneNumberLogSanitizer.Redact(ex.Message));
                try
                {
                    var latest = await _twilio.FetchMessageAsync(followUp.ProviderMessageSid!, cancellationToken);
                    ApplySnapshot(followUp, latest);
                }
                catch
                {
                    // Best-effort refresh; the order cancel itself already succeeded.
                }
            }

            await _notifications.UpdateAsync(followUp, cancellationToken);
        }
    }

    private async Task<ContactNumber?> GetCurrentContactAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerIdSpecification(buyerId), cancellationToken);
        return numbers.FirstOrDefault();
    }

    private async Task<bool> DestinationStillRegisteredAsync(OrderNotification source, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerIdSpecification(source.BuyerId), cancellationToken);
        return numbers.Any(n => n.CanonicalNumber == source.DestinationNumber);
    }

    private static void ApplySnapshot(OrderNotification notification, TwilioMessageSnapshot snapshot)
    {
        notification.ApplyProviderResult(snapshot.Sid, snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage);
        if (string.Equals(snapshot.Status, "scheduled", StringComparison.OrdinalIgnoreCase) && notification.ScheduledSendAt is null)
        {
            notification.MarkScheduled(DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay));
        }
    }

    private Task<string> ResolveFromNumberAsync()
    {
        // FromNumber is owned by the Twilio client configuration; list filtering uses the same value.
        return Task.FromResult(_twilio.ConfiguredFromNumber);
    }
}
