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
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly IOrderMessagingGateway _messaging;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notifications,
        IRepository<ShopperContactNumber> contactNumbers,
        IOrderMessagingGateway messaging,
        IAppLogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _messaging = messaging;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken)
        => TryNotifyAsync(order, NotificationKind.Placed, OrderSmsCopy.Placed(order.Id), sendAt: null, cancellationToken);

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken)
    {
        await TryNotifyAsync(order, NotificationKind.Dispatched, OrderSmsCopy.Dispatched(order.Id), sendAt: null, cancellationToken);
        await TryNotifyAsync(
            order,
            NotificationKind.FollowUp,
            OrderSmsCopy.FollowUp(order.Id),
            DateTimeOffset.UtcNow.Add(FollowUpDelay),
            cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken)
    {
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);
        await TryNotifyAsync(order, NotificationKind.Cancelled, OrderSmsCopy.Cancelled(order.Id), sendAt: null, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var items = await _notifications.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        var owned = items.Where(n => n.BuyerId == buyerId).ToList();
        if (owned.Count == 0 && items.Count > 0)
        {
            return Array.Empty<OrderNotification>();
        }

        await RefreshAsync(owned, cancellationToken);
        return owned;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken)
    {
        var items = await _notifications.ListAsync(new NotificationsByBuyerSpecification(buyerId), cancellationToken);
        await RefreshAsync(items, cancellationToken);
        return items;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        var existing = await _notifications.FirstOrDefaultAsync(
            new ResendByParentAndKeySpecification(notificationId, idempotencyKey),
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException("Notification was not found.");

        if (!await IsDestinationStillOnFile(original.BuyerId, original.DestinationNumber, cancellationToken))
        {
            throw new InvalidOperationException("The destination number is no longer on file.");
        }

        var body = original.ContentRedacted || string.IsNullOrEmpty(original.Body)
            ? OrderSmsCopy.ForKind(original.Kind, original.OrderId)
            : original.Body;

        var sent = await _messaging.SendAsync(original.DestinationNumber, body, sendAt: null, cancellationToken);
        var notification = FromProvider(
            original.OrderId,
            original.BuyerId,
            original.Kind,
            sent,
            body,
            original.DestinationNumber,
            sendAt: null,
            resentFromNotificationId: original.Id,
            idempotencyKey: idempotencyKey);
        return await _notifications.AddAsync(notification, cancellationToken);
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException("Notification was not found.");

        if (!string.IsNullOrEmpty(notification.ProviderSid))
        {
            var updated = await _messaging.RedactBodyAsync(notification.ProviderSid, cancellationToken);
            notification.ApplyProviderState(
                updated.Sid,
                updated.Status,
                updated.Body,
                updated.From,
                updated.MessagingServiceSid,
                updated.Direction,
                updated.DateCreated,
                updated.DateSent,
                updated.DateUpdated,
                updated.ErrorCode,
                updated.ErrorMessage);
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var (provider, truncated) = await _messaging.ListSentFromConfiguredNumberAsync(from, to, cancellationToken);
        var providerSids = provider.Select(m => m.Sid).Where(s => !string.IsNullOrEmpty(s)).Cast<string>().ToList();
        var localBySidEntities = providerSids.Count == 0
            ? new List<OrderNotification>()
            : await _notifications.ListAsync(new NotificationsByProviderSidsSpecification(providerSids), cancellationToken);
        var localBySid = localBySidEntities
            .Where(n => !string.IsNullOrEmpty(n.ProviderSid))
            .GroupBy(n => n.ProviderSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var local = await _notifications.ListAsync(new NotificationsInCreatedRangeSpecification(from, to), cancellationToken);

        var matched = new List<ReconciliationItem>();
        var onlyProvider = new List<ReconciliationItem>();
        var seenSids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var message in provider)
        {
            if (string.IsNullOrEmpty(message.Sid))
            {
                continue;
            }

            seenSids.Add(message.Sid);
            if (localBySid.TryGetValue(message.Sid, out var notification))
            {
                matched.Add(ToItem(message, notification, "matched"));
            }
            else
            {
                onlyProvider.Add(ToItem(message, null, "provider"));
            }
        }

        var onlyApp = local
            .Where(n => string.IsNullOrEmpty(n.ProviderSid) || !seenSids.Contains(n.ProviderSid))
            .Select(n => new ReconciliationItem(n.ProviderSid, n.Id, n.Status, n.ContentRedacted ? null : n.Body, n.ProviderDateSent, "application"))
            .ToList();

        return new ReconciliationReport(from, to, matched, onlyProvider, onlyApp, truncated);
    }

    private async Task TryNotifyAsync(
        Order order,
        NotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        ShopperContactNumber? destination;
        try
        {
            destination = await GetCurrentDestination(order.BuyerId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Order {OrderId} notification {Kind} skipped: destination lookup failed ({Exception}).", order.Id, kind, ex.GetType().Name);
            return;
        }

        if (destination is null)
        {
            return;
        }

        try
        {
            var sent = await _messaging.SendAsync(destination.CanonicalNumber, body, sendAt, cancellationToken);
            var notification = FromProvider(
                order.Id,
                order.BuyerId,
                kind,
                sent,
                body,
                destination.CanonicalNumber,
                sendAt,
                resentFromNotificationId: null,
                idempotencyKey: null);
            await _notifications.AddAsync(notification, cancellationToken);
            _logger.LogInformation("Order {OrderId} notification {Kind} accepted as {Sid} with status {Status}.", order.Id, kind, sent.Sid ?? "", sent.Status ?? "");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Order {OrderId} notification {Kind} failed ({Exception}). The order action still succeeded.", order.Id, kind, ex.GetType().Name);
            var failed = new OrderNotification(
                order.Id,
                order.BuyerId,
                kind,
                providerSid: null,
                status: "failed",
                body: body,
                destinationNumber: destination.CanonicalNumber,
                fromNumber: null,
                messagingServiceSid: null,
                direction: null,
                dateCreated: null,
                dateSent: null,
                dateUpdated: null,
                errorCode: ex is OrderMessagingException ome ? ome.HttpStatus : null,
                errorMessage: "The provider did not accept the message.",
                sendAt: sendAt,
                resentFromNotificationId: null,
                idempotencyKey: null);
            try
            {
                await _notifications.AddAsync(failed, cancellationToken);
            }
            catch (Exception persistEx)
            {
                _logger.LogWarning("Order {OrderId} failed-notification persist failed ({Exception}).", order.Id, persistEx.GetType().Name);
            }
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var pending = await _notifications.ListAsync(new ScheduledFollowUpsByOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in pending)
        {
            if (string.IsNullOrEmpty(followUp.ProviderSid))
            {
                continue;
            }

            try
            {
                var updated = await _messaging.CancelScheduledAsync(followUp.ProviderSid, cancellationToken);
                followUp.ApplyProviderState(
                    updated.Sid,
                    updated.Status,
                    updated.Body,
                    updated.From,
                    updated.MessagingServiceSid,
                    updated.Direction,
                    updated.DateCreated,
                    updated.DateSent,
                    updated.DateUpdated,
                    updated.ErrorCode,
                    updated.ErrorMessage);
                await _notifications.UpdateAsync(followUp, cancellationToken);
                _logger.LogInformation("Order {OrderId} follow-up {Sid} cancel requested; status {Status}.", orderId, followUp.ProviderSid ?? "", updated.Status ?? "");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Order {OrderId} follow-up cancel failed ({Exception}). The cancel action still succeeded.", orderId, ex.GetType().Name);
            }
        }
    }

    private async Task RefreshAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderSid))
            {
                continue;
            }

            try
            {
                var latest = await _messaging.FetchAsync(notification.ProviderSid, cancellationToken);
                notification.ApplyProviderState(
                    latest.Sid,
                    latest.Status,
                    notification.ContentRedacted ? null : latest.Body,
                    latest.From,
                    latest.MessagingServiceSid,
                    latest.Direction,
                    latest.DateCreated,
                    latest.DateSent,
                    latest.DateUpdated,
                    latest.ErrorCode,
                    latest.ErrorMessage);
                if (notification.ContentRedacted)
                {
                    notification.MarkContentRedacted();
                }

                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Notification {NotificationId} status refresh failed ({Exception}).", notification.Id, ex.GetType().Name);
            }
        }
    }

    private async Task<ShopperContactNumber?> GetCurrentDestination(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers.OrderByDescending(n => n.Id).FirstOrDefault();
    }

    private async Task<bool> IsDestinationStillOnFile(string buyerId, string canonicalNumber, CancellationToken cancellationToken)
    {
        var match = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpecification(buyerId, canonicalNumber),
            cancellationToken);
        return match is not null;
    }

    private static OrderNotification FromProvider(
        int orderId,
        string buyerId,
        NotificationKind kind,
        ProviderMessage sent,
        string body,
        string destination,
        DateTimeOffset? sendAt,
        int? resentFromNotificationId,
        string? idempotencyKey)
    {
        return new OrderNotification(
            orderId,
            buyerId,
            kind,
            sent.Sid,
            sent.Status,
            body,
            destination,
            sent.From,
            sent.MessagingServiceSid,
            sent.Direction,
            sent.DateCreated,
            sent.DateSent,
            sent.DateUpdated,
            sent.ErrorCode,
            sent.ErrorMessage,
            sendAt,
            resentFromNotificationId,
            idempotencyKey);
    }

    private static ReconciliationItem ToItem(ProviderMessage message, OrderNotification? notification, string source)
    {
        return new ReconciliationItem(
            message.Sid,
            notification?.Id,
            message.Status ?? notification?.Status,
            notification is { ContentRedacted: true } ? null : (message.Body ?? notification?.Body),
            message.DateSent ?? notification?.ProviderDateSent,
            source);
    }
}

internal static class OrderSmsCopy
{
    public static string Placed(int orderId) => $"Your eShopOnWeb order #{orderId} has been placed.";
    public static string Dispatched(int orderId) => $"Your eShopOnWeb order #{orderId} is on its way.";
    public static string FollowUp(int orderId) => $"How did the delivery of your eShopOnWeb order #{orderId} go?";
    public static string Cancelled(int orderId) => $"Your eShopOnWeb order #{orderId} has been cancelled.";

    public static string ForKind(NotificationKind kind, int orderId) => kind switch
    {
        NotificationKind.Placed => Placed(orderId),
        NotificationKind.Dispatched => Dispatched(orderId),
        NotificationKind.FollowUp => FollowUp(orderId),
        NotificationKind.Cancelled => Cancelled(orderId),
        _ => $"Update regarding your eShopOnWeb order #{orderId}."
    };
}
