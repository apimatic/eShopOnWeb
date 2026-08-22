using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ISmsMessagingClient _messaging;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notifications,
        IRepository<ContactNumber> contactNumbers,
        ISmsMessagingClient messaging,
        IAppLogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _messaging = messaging;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"Your eShop order #{order.Id} has been placed. Total: {order.Total():0.00}.";
        return TrySendAsync(order, NotificationKind.OrderPlaced, body, sendAt: null, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var dispatchBody = $"Your eShop order #{order.Id} is on its way.";
        await TrySendAsync(order, NotificationKind.OrderDispatched, dispatchBody, sendAt: null, cancellationToken);

        var followUpBody = $"How did the delivery of eShop order #{order.Id} go?";
        await TrySendAsync(order, NotificationKind.DeliveryFollowUp, followUpBody, DateTimeOffset.UtcNow.Add(FollowUpDelay), cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        var body = $"Your eShop order #{order.Id} has been cancelled.";
        await TrySendAsync(order, NotificationKind.OrderCancelled, body, sendAt: null, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notifications.ListAsync(new NotificationsByOrderIdSpecification(orderId), cancellationToken);
        await RefreshFromProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrdersAsync(IEnumerable<int> orderIds, CancellationToken cancellationToken = default)
    {
        var ids = orderIds.ToList();
        if (ids.Count == 0)
        {
            return Array.Empty<OrderNotification>();
        }

        var notifications = await _notifications.ListAsync(new NotificationsByOrderIdsSpecification(ids), cancellationToken);
        await RefreshFromProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.");
        }

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException("Notification was not found.");

        var existing = await _notifications.FirstOrDefaultAsync(
            new NotificationResendByKeySpecification(source.Id, idempotencyKey),
            cancellationToken);
        if (existing is not null)
        {
            await RefreshFromProviderAsync(new[] { existing }, cancellationToken);
            return existing;
        }

        if (!await DestinationStillRegisteredAsync(source.BuyerId, source.DestinationNumber, cancellationToken))
        {
            throw new InvalidPhoneNumberException("The destination is no longer on file and cannot be messaged again.");
        }

        if (source.ContentRedacted || string.IsNullOrEmpty(source.Body))
        {
            throw new InvalidOperationException("The original message content is no longer available to resend.");
        }

        var resend = new OrderNotification(
            source.OrderId,
            source.BuyerId,
            NotificationKind.Resend,
            source.DestinationNumber,
            source.Body,
            scheduledFor: null,
            sourceNotificationId: source.Id,
            idempotencyKey: idempotencyKey);

        resend = await _notifications.AddAsync(resend, cancellationToken);
        await DispatchToProviderAsync(resend, sendAt: null, cancellationToken);
        return resend;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException("Notification was not found.");

        if (!string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            try
            {
                var result = await _messaging.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
                notification.RecordProviderResult(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to redact provider content for notification {NotificationId}: {Message}", notification.Id, ex.Message);
                throw;
            }
        }

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The 'to' timestamp must be on or after 'from'.");
        }

        var providerMessages = await _messaging.ListSentFromAsync(from, to, cancellationToken);
        var localInRange = await _notifications.ListAsync(new NotificationsInCreatedRangeSpecification(from, to), cancellationToken);
        var providerSids = providerMessages.Where(m => !string.IsNullOrWhiteSpace(m.Sid)).Select(m => m.Sid!).ToList();
        var localBySidMatches = providerSids.Count == 0
            ? new List<OrderNotification>()
            : await _notifications.ListAsync(new NotificationsByProviderSidsSpecification(providerSids), cancellationToken);

        var local = localInRange
            .Concat(localBySidMatches)
            .GroupBy(n => n.Id)
            .Select(g => g.First())
            .ToList();

        var localBySid = local
            .Where(n => !string.IsNullOrWhiteSpace(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrWhiteSpace(m.Sid))
            .GroupBy(m => m.Sid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var items = new List<ReconciliationItem>();

        foreach (var sid in providerBySid.Keys.Union(localBySid.Keys, StringComparer.Ordinal))
        {
            providerBySid.TryGetValue(sid, out var provider);
            localBySid.TryGetValue(sid, out var localNote);

            var match = provider is not null && localNote is not null
                ? "matched"
                : provider is not null
                    ? "provider_only"
                    : "local_only";

            items.Add(new ReconciliationItem(
                LocalNotificationId: localNote?.Id.ToString(),
                ProviderMessageSid: sid,
                Match: match,
                LocalStatus: localNote?.ProviderStatus,
                ProviderStatus: provider?.Status,
                DateCreated: provider?.DateCreated ?? localNote?.CreatedAt,
                DateSent: provider?.DateSent));
        }

        items = items
            .OrderBy(i => i.DateCreated ?? DateTimeOffset.MaxValue)
            .ToList();

        return new ReconciliationReport(
            from,
            to,
            FromNumber: _messaging.FromNumber,
            items,
            MatchedCount: items.Count(i => i.Match == "matched"),
            ProviderOnlyCount: items.Count(i => i.Match == "provider_only"),
            LocalOnlyCount: items.Count(i => i.Match == "local_only"));
    }

    private async Task TrySendAsync(
        Order order,
        NotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var destination = await GetActiveDestinationAsync(order.BuyerId, cancellationToken);
            if (destination is null)
            {
                return;
            }

            var notification = new OrderNotification(order.Id, order.BuyerId, kind, destination, body, sendAt);
            notification = await _notifications.AddAsync(notification, cancellationToken);
            await DispatchToProviderAsync(notification, sendAt, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Order notification {Kind} for order {OrderId} did not send: {Message}", kind, order.Id, ex.Message);
        }
    }

    private async Task DispatchToProviderAsync(
        OrderNotification notification,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _messaging.SendAsync(new SendSmsRequest(notification.DestinationNumber, notification.Body, sendAt), cancellationToken);
            notification.RecordProviderResult(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage);
        }
        catch (Exception ex)
        {
            notification.MarkSendFailed(ex.Message);
            _logger.LogWarning("Provider send failed for notification {NotificationId}: {Message}", notification.Id, ex.Message);
        }

        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var pending = await _notifications.ListAsync(new PendingFollowUpByOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in pending)
        {
            try
            {
                var result = await _messaging.CancelAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.RecordProviderResult(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage);
            }
            catch (Exception ex)
            {
                followUp.MarkSendFailed(ex.Message);
                _logger.LogWarning("Failed to cancel follow-up notification {NotificationId}: {Message}", followUp.Id, ex.Message);
            }

            await _notifications.UpdateAsync(followUp, cancellationToken);
        }
    }

    private async Task RefreshFromProviderAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var result = await _messaging.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                notification.RecordProviderResult(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage);
                if (notification.ContentRedacted)
                {
                    notification.RedactContent();
                }
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not refresh notification {NotificationId} from provider: {Message}", notification.Id, ex.Message);
            }
        }
    }

    private async Task<string?> GetActiveDestinationAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers.FirstOrDefault()?.CanonicalNumber;
    }

    private async Task<bool> DestinationStillRegisteredAsync(string buyerId, string destinationNumber, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers.Any(n => n.CanonicalNumber == destinationNumber);
    }
}
