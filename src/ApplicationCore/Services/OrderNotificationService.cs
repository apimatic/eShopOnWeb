using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly ITwilioMessagingClient _twilio;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notificationRepository,
        IRepository<ContactNumber> contactNumberRepository,
        ITwilioMessagingClient twilio,
        IAppLogger<OrderNotificationService> logger)
    {
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _twilio = twilio;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default) =>
        NotifyAsync(order, NotificationType.OrderPlaced,
            $"Your eShop order #{order.Id} has been placed. Thank you for shopping with us.",
            scheduleFor: null, cancellationToken);

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await NotifyAsync(order, NotificationType.OrderDispatched,
            $"Your eShop order #{order.Id} is on its way.",
            scheduleFor: null, cancellationToken);

        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        await NotifyAsync(order, NotificationType.DeliveryFollowUp,
            $"How did the delivery of eShop order #{order.Id} go?",
            sendAt, cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);
        await NotifyAsync(order, NotificationType.OrderCancelled,
            $"Your eShop order #{order.Id} has been cancelled.",
            scheduleFor: null, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.ListAsync(
            new OrderNotificationsByOrderIdSpec(orderId), cancellationToken);
        await RefreshFromProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrdersAsync(IEnumerable<int> orderIds, CancellationToken cancellationToken = default)
    {
        var ids = orderIds.ToArray();
        if (ids.Length == 0)
        {
            return Array.Empty<OrderNotification>();
        }

        var notifications = await _notificationRepository.ListAsync(
            new OrderNotificationsByOrderIdsSpec(ids), cancellationToken);
        await RefreshFromProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new ResendByIdempotencySpec(notificationId, idempotencyKey.Trim()), cancellationToken);
        if (existing is not null)
        {
            await RefreshFromProviderAsync(new[] { existing }, cancellationToken);
            return existing;
        }

        var source = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException("Notification was not found.");

        if (source.ContentRedacted || string.IsNullOrEmpty(source.Body))
        {
            throw new InvalidOperationException("The message content has been disposed of and cannot be re-sent.");
        }

        if (source.DestinationContactId is null)
        {
            throw new InvalidOperationException("This notification has no destination on file.");
        }

        var destination = await _contactNumberRepository.GetByIdAsync(source.DestinationContactId.Value, cancellationToken)
            ?? throw new InvalidOperationException("The original destination is no longer on file and must not be messaged again.");

        var resent = new OrderNotification(source.ForOrderId, source.BuyerId, destination.Id, source.Type, source.Body);
        resent.AttachResend(source.Id, idempotencyKey.Trim());
        await SendAndPersistAsync(resent, destination.CanonicalNumber, scheduleFor: null, cancellationToken);
        return resent;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException("Notification was not found.");

        if (!string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            var result = await _twilio.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
            notification.ApplyProviderResult(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage);
        }

        notification.RedactContent();
        await _notificationRepository.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The end of the range must not precede the start.");
        }

        var fromNumber = _twilio.FromNumber;

        var providerMessages = await _twilio.ListFromNumberAsync(fromNumber, from, to, cancellationToken);
        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var known = providerBySid.Count == 0
            ? new List<OrderNotification>()
            : await _notificationRepository.ListAsync(
                new OrderNotificationsByProviderSidsSpec(providerBySid.Keys), cancellationToken);

        var knownBySid = known
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .ToDictionary(n => n.ProviderMessageSid!, StringComparer.Ordinal);

        var matched = new List<ReconciliationMatch>();
        var providerOnly = new List<TwilioMessageResult>();
        foreach (var message in providerMessages)
        {
            if (!string.IsNullOrEmpty(message.Sid) && knownBySid.TryGetValue(message.Sid, out var notification))
            {
                matched.Add(new ReconciliationMatch(notification, message));
            }
            else
            {
                providerOnly.Add(message);
            }
        }

        var oursInRange = await _notificationRepository.ListAsync(
            new OrderNotificationsByCreatedRangeSpec(from, to), cancellationToken);

        var matchedSids = matched
            .Select(m => m.ProviderMessage.Sid)
            .Where(s => !string.IsNullOrEmpty(s))
            .ToHashSet(StringComparer.Ordinal);

        var applicationOnly = oursInRange
            .Where(n => string.IsNullOrEmpty(n.ProviderMessageSid) || !matchedSids.Contains(n.ProviderMessageSid!))
            .ToList();

        return new ReconciliationReport(from, to, fromNumber, matched, providerOnly, applicationOnly);
    }

    private async Task NotifyAsync(
        Order order,
        NotificationType type,
        string body,
        DateTimeOffset? scheduleFor,
        CancellationToken cancellationToken)
    {
        var destinations = await _contactNumberRepository.ListAsync(
            new ContactNumbersByBuyerSpec(order.BuyerId), cancellationToken);
        if (destinations.Count == 0)
        {
            _logger.LogInformation("No contact number on file for buyer {BuyerId}; skipping {Type} for order {OrderId}.",
                order.BuyerId, type, order.Id);
            return;
        }

        foreach (var destination in destinations)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, destination.Id, type, body);
            await SendAndPersistAsync(notification, destination.CanonicalNumber, scheduleFor, cancellationToken);
        }
    }

    private async Task SendAndPersistAsync(
        OrderNotification notification,
        string destination,
        DateTimeOffset? scheduleFor,
        CancellationToken cancellationToken)
    {
        if (scheduleFor.HasValue)
        {
            notification.MarkScheduled(scheduleFor.Value);
        }

        try
        {
            var result = scheduleFor.HasValue
                ? await _twilio.ScheduleAsync(destination, notification.Body ?? string.Empty, scheduleFor.Value, cancellationToken)
                : await _twilio.SendAsync(destination, notification.Body ?? string.Empty, cancellationToken);

            notification.ApplyProviderResult(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage);
            _logger.LogInformation(
                "Recorded {Type} notification {NotificationId} for order {OrderId} with provider status {Status}.",
                notification.Type, notification.Id, notification.ForOrderId, result.Status);
        }
        catch (Exception ex)
        {
            notification.MarkSendFailed("The messaging provider rejected or failed the send.");
            _logger.LogWarning(
                "Failed to send {Type} notification for order {OrderId}: {Error}",
                notification.Type, notification.ForOrderId, PhoneNumberRedactor.Redact(ex.Message));
        }

        if (notification.Id == 0)
        {
            await _notificationRepository.AddAsync(notification, cancellationToken);
        }
        else
        {
            await _notificationRepository.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var pending = await _notificationRepository.ListAsync(new ScheduledFollowUpsByOrderSpec(orderId), cancellationToken);
        foreach (var followUp in pending)
        {
            try
            {
                var result = await _twilio.CancelScheduledAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.ApplyProviderResult(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage);
                _logger.LogInformation(
                    "Cancelled scheduled follow-up {NotificationId} for order {OrderId}; provider status {Status}.",
                    followUp.Id, orderId, result.Status);
            }
            catch (Exception ex)
            {
                followUp.MarkSendFailed("Failed to cancel the scheduled follow-up with the provider.");
                _logger.LogWarning(
                    "Failed to cancel scheduled follow-up {NotificationId} for order {OrderId}: {Error}",
                    followUp.Id, orderId, PhoneNumberRedactor.Redact(ex.Message));
            }

            await _notificationRepository.SaveChangesAsync(cancellationToken);
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
                var result = await _twilio.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                notification.ApplyProviderResult(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage);
                if (notification.ContentRedacted)
                {
                    notification.RedactContent();
                }
                await _notificationRepository.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Could not refresh provider status for notification {NotificationId}: {Error}",
                    notification.Id, PhoneNumberRedactor.Redact(ex.Message));
            }
        }
    }
}

internal static class PhoneNumberRedactor
{
    private static readonly System.Text.RegularExpressions.Regex PhoneLike =
        new(@"\+\d{8,}", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static string Redact(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : PhoneLike.Replace(text, "[redacted]");
}
