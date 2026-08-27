using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Extensions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly ITwilioMessagingClient _messagingClient;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        ITwilioMessagingClient messagingClient,
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        IAppLogger<OrderNotificationService> logger)
    {
        _messagingClient = messagingClient;
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        return NotifyAsync(
            order,
            NotificationKind.OrderPlaced,
            $"Your eShopOnWeb order #{order.Id} has been placed. Thank you for your purchase.",
            sendAt: null,
            cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await NotifyAsync(
            order,
            NotificationKind.OrderDispatched,
            $"Your eShopOnWeb order #{order.Id} is on its way.",
            sendAt: null,
            cancellationToken);

        await NotifyAsync(
            order,
            NotificationKind.DeliveryFollowUp,
            $"How did the delivery of your eShopOnWeb order #{order.Id} go? We would love to hear from you.",
            DateTimeOffset.UtcNow.Add(FollowUpDelay),
            cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        await NotifyAsync(
            order,
            NotificationKind.OrderCancelled,
            $"Your eShopOnWeb order #{order.Id} has been cancelled.",
            sendAt: null,
            cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.ListAsync(
            new OrderNotificationsByOrderIdSpecification(orderId),
            cancellationToken);
        await RefreshFromProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrdersAsync(IReadOnlyCollection<int> orderIds, CancellationToken cancellationToken = default)
    {
        if (orderIds.Count == 0)
        {
            return Array.Empty<OrderNotification>();
        }

        var notifications = await _notificationRepository.ListAsync(
            new OrderNotificationsByOrderIdsSpecification(orderIds),
            cancellationToken);
        await RefreshFromProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task RefreshFromProviderAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var provider = await _messagingClient.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                await ApplyAndPersistAsync(notification, provider, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Could not refresh notification {NotificationId} from the provider: {Message}",
                    notification.Id,
                    LogRedaction.RedactPhoneNumbers(ex.Message));
            }
        }
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new ResendByIdempotencyKeySpecification(notificationId, idempotencyKey.Trim()),
            cancellationToken);
        if (existing is not null)
        {
            await RefreshFromProviderAsync(new[] { existing }, cancellationToken);
            return existing;
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            throw new KeyNotFoundException("Notification was not found.");
        }

        var destinationStillOnFile = await DestinationIsOnFileAsync(original.BuyerId, original.Destination, cancellationToken);
        if (!destinationStillOnFile)
        {
            throw new InvalidOperationException("This destination is no longer on file and cannot be messaged again.");
        }

        if (!string.IsNullOrWhiteSpace(original.ProviderMessageSid))
        {
            try
            {
                var provider = await _messagingClient.FetchAsync(original.ProviderMessageSid, cancellationToken);
                await ApplyAndPersistAsync(original, provider, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Could not refresh notification {NotificationId} before resend: {Message}",
                    original.Id,
                    LogRedaction.RedactPhoneNumbers(ex.Message));
            }
        }

        if (!IsFailedDelivery(original.Status))
        {
            throw new InvalidOperationException($"Notification {original.Id} has status '{original.Status}' and does not need a resend.");
        }

        var body = original.ContentRedacted || string.IsNullOrWhiteSpace(original.Body)
            ? BuildBody(original.Kind, original.OrderId)
            : original.Body;

        var resent = await SendAndRecordAsync(
            original.OrderId,
            original.BuyerId,
            NotificationKind.Resend,
            original.Destination,
            body,
            sendAt: null,
            original.Id,
            idempotencyKey.Trim(),
            cancellationToken);

        return resent;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            throw new KeyNotFoundException("Notification was not found.");
        }

        if (!string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            var provider = await _messagingClient.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
            notification.ApplyProviderState(
                provider.Status,
                provider.ErrorCode,
                provider.ErrorMessage,
                provider.DateSent,
                body: null);
        }

        notification.MarkContentRedacted();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The 'to' timestamp must be on or after 'from'.");
        }

        var fromNumber = _messagingClient.ConfiguredFromNumber;
        var providerMessages = await _messagingClient.ListSentFromAsync(from, to, cancellationToken);
        var local = await _notificationRepository.ListAsync(new NotificationsInDateRangeSpecification(from, to), cancellationToken);

        var localBySid = local
            .Where(n => !string.IsNullOrWhiteSpace(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var matched = new List<ReconciliationMatch>();
        var providerOnly = new List<ProviderMessageResult>();
        var matchedSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in providerMessages)
        {
            if (string.IsNullOrWhiteSpace(provider.Sid))
            {
                continue;
            }

            if (localBySid.TryGetValue(provider.Sid, out var localNotification))
            {
                matched.Add(new ReconciliationMatch(localNotification, provider));
                matchedSids.Add(provider.Sid);
            }
            else
            {
                providerOnly.Add(provider);
            }
        }

        var localOnly = local
            .Where(n => string.IsNullOrWhiteSpace(n.ProviderMessageSid) || !matchedSids.Contains(n.ProviderMessageSid))
            .ToList();

        return new ReconciliationReport(from, to, fromNumber, matched, providerOnly, localOnly);
    }

    private async Task NotifyAsync(
        Order order,
        NotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var destinations = await _contactNumberRepository.ListAsync(
                new ContactNumbersByBuyerSpecification(order.BuyerId),
                cancellationToken);
            if (destinations.Count == 0)
            {
                return;
            }

            foreach (var destination in destinations)
            {
                await SendAndRecordAsync(
                    order.Id,
                    order.BuyerId,
                    kind,
                    destination.PhoneNumber,
                    body,
                    sendAt,
                    originalNotificationId: null,
                    idempotencyKey: null,
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Order {OrderId} {Kind} notification failed: {Message}",
                order.Id,
                kind,
                LogRedaction.RedactPhoneNumbers(ex.Message));
        }
    }

    private async Task<OrderNotification> SendAndRecordAsync(
        int orderId,
        string buyerId,
        NotificationKind kind,
        string destination,
        string body,
        DateTimeOffset? sendAt,
        int? originalNotificationId,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        ProviderMessageResult? provider = null;
        try
        {
            provider = await _messagingClient.SendAsync(new SendMessageRequest(destination, body, sendAt), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Failed to send {Kind} notification for order {OrderId}: {Message}",
                kind,
                orderId,
                LogRedaction.RedactPhoneNumbers(ex.Message));
        }

        var notification = new OrderNotification(
            orderId,
            buyerId,
            kind,
            destination,
            body,
            provider?.Sid,
            provider?.Status ?? "send_failed",
            provider?.ErrorCode,
            provider?.ErrorMessage,
            sendAt,
            originalNotificationId,
            idempotencyKey);

        if (provider?.DateSent is not null)
        {
            notification.ApplyProviderState(
                notification.Status,
                notification.ErrorCode,
                notification.ErrorMessage,
                provider.DateSent,
                body);
        }

        await _notificationRepository.AddAsync(notification, cancellationToken);
        return notification;
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notificationRepository.ListAsync(
            new PendingFollowUpNotificationsSpecification(orderId),
            cancellationToken);

        foreach (var followUp in followUps)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(followUp.ProviderMessageSid))
                {
                    var provider = await _messagingClient.FetchAsync(followUp.ProviderMessageSid, cancellationToken);
                    await ApplyAndPersistAsync(followUp, provider, cancellationToken);
                    if (!followUp.IsPendingSend())
                    {
                        continue;
                    }

                    provider = await _messagingClient.CancelAsync(followUp.ProviderMessageSid, cancellationToken);
                    await ApplyAndPersistAsync(followUp, provider, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Failed to cancel follow-up notification {NotificationId} for order {OrderId}: {Message}",
                    followUp.Id,
                    orderId,
                    LogRedaction.RedactPhoneNumbers(ex.Message));
            }
        }
    }

    private async Task ApplyAndPersistAsync(
        OrderNotification notification,
        ProviderMessageResult provider,
        CancellationToken cancellationToken)
    {
        var body = notification.ContentRedacted ? null : provider.Body;
        notification.ApplyProviderState(provider.Status, provider.ErrorCode, provider.ErrorMessage, provider.DateSent, body);
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    private async Task<bool> DestinationIsOnFileAsync(string buyerId, string destination, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumberRepository.ListAsync(
            new ContactNumbersByBuyerSpecification(buyerId),
            cancellationToken);
        return numbers.Any(n => string.Equals(n.PhoneNumber, destination, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsFailedDelivery(string status)
    {
        return string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "undelivered", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "send_failed", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildBody(NotificationKind kind, int orderId)
    {
        return kind switch
        {
            NotificationKind.OrderPlaced => $"Your eShopOnWeb order #{orderId} has been placed. Thank you for your purchase.",
            NotificationKind.OrderDispatched => $"Your eShopOnWeb order #{orderId} is on its way.",
            NotificationKind.DeliveryFollowUp => $"How did the delivery of your eShopOnWeb order #{orderId} go? We would love to hear from you.",
            NotificationKind.OrderCancelled => $"Your eShopOnWeb order #{orderId} has been cancelled.",
            _ => $"An update about your eShopOnWeb order #{orderId}."
        };
    }
}
