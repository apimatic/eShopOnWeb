using System;
using System.Collections.Concurrent;
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
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);
    private static readonly ConcurrentDictionary<int, byte> RedactedIds = new();

    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly ISmsProvider _sms;
    private readonly INotificationPersistence _persistence;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notifications,
        IRepository<ShopperContactNumber> contactNumbers,
        ISmsProvider sms,
        INotificationPersistence persistence,
        IAppLogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _sms = sms;
        _persistence = persistence;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken)
        => TrySendImmediateAsync(order, NotificationKind.OrderPlaced,
            $"Your eShopOnWeb order #{order.Id} has been placed. Total: {order.Total():0.00}.",
            cancellationToken);

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken)
    {
        await TrySendImmediateAsync(order, NotificationKind.OrderDispatched,
            $"Your eShopOnWeb order #{order.Id} is on its way.",
            cancellationToken);

        var destination = await GetPrimaryDestinationAsync(order.BuyerId, cancellationToken);
        if (destination is null)
            return;

        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var body = $"How did the delivery of eShopOnWeb order #{order.Id} go?";
        var notification = new OrderNotification(order.Id, order.BuyerId, NotificationKind.DeliveryFollowUp, body, destination);
        notification.SetSchedule(sendAt);
        notification = await _notifications.AddAsync(notification, cancellationToken);

        try
        {
            var result = await _sms.ScheduleAsync(destination, body, sendAt, cancellationToken);
            ApplyDispatchResult(notification, result);
        }
        catch (Exception)
        {
            notification.RecordSendFailure("The follow-up could not be queued with the provider.");
            _logger.LogWarning("Failed to queue delivery follow-up for order {OrderId}", order.Id);
        }

        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken)
    {
        await CancelFollowUpsAsync(
            await _notifications.ListAsync(new ScheduledFollowUpsByOrderSpec(order.Id), cancellationToken),
            cancellationToken);

        await TrySendImmediateAsync(order, NotificationKind.OrderCancelled,
            $"Your eShopOnWeb order #{order.Id} has been cancelled.",
            cancellationToken);
    }

    public async Task CancelPendingFollowUpsForDestinationAsync(string buyerId, string destinationCanonical, CancellationToken cancellationToken)
    {
        var followUps = await _notifications.ListAsync(
            new ScheduledFollowUpsByDestinationSpec(buyerId, destinationCanonical), cancellationToken);
        await CancelFollowUpsAsync(followUps, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, string? buyerId, CancellationToken cancellationToken)
    {
        IReadOnlyList<OrderNotification> items = buyerId is null
            ? await _notifications.ListAsync(new NotificationsByOrderIdSpec(orderId), cancellationToken)
            : await _notifications.ListAsync(new NotificationsByOrderIdAndBuyerSpec(orderId, buyerId), cancellationToken);

        await RefreshFromProviderAsync(items, cancellationToken);
        ApplyLocalRedaction(items);
        return items;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForBuyerOrdersAsync(string buyerId, IReadOnlyList<int> orderIds, CancellationToken cancellationToken)
    {
        if (orderIds.Count == 0)
            return Array.Empty<OrderNotification>();

        var items = await _notifications.ListAsync(new NotificationsByBuyerAndOrdersSpec(buyerId, orderIds), cancellationToken);
        await RefreshFromProviderAsync(items, cancellationToken);
        ApplyLocalRedaction(items);
        return items;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new NotificationResendNotAllowedException("An idempotency key is required.");

        var existingResend = await _notifications.FirstOrDefaultAsync(
            new ResendByParentAndKeySpec(notificationId, idempotencyKey), cancellationToken);
        if (existingResend is not null)
            return existingResend;

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken)
                       ?? throw new NotificationNotFoundException(notificationId);

        await RefreshFromProviderAsync(new[] { original }, cancellationToken);

        if (!original.DidNotReachShopper())
            throw new NotificationResendNotAllowedException("Only messages that did not reach the shopper can be re-sent.");

        var destination = await GetRegisteredDestinationAsync(original.BuyerId, original.DestinationCanonical, cancellationToken);
        if (destination is null)
            throw new NotificationResendNotAllowedException("The destination number is no longer on file for this shopper.");

        var body = string.IsNullOrWhiteSpace(original.Body) || original.ContentRedacted || RedactedIds.ContainsKey(original.Id)
            ? $"Update on your eShopOnWeb order #{original.OrderId}."
            : original.Body;

        var resend = new OrderNotification(original.OrderId, original.BuyerId, NotificationKind.Resend, body, destination);
        resend.SetResendMetadata(original.Id, idempotencyKey);
        resend = await _notifications.AddAsync(resend, cancellationToken);

        try
        {
            var result = await _sms.SendImmediateAsync(destination, body, cancellationToken);
            ApplyDispatchResult(resend, result);
        }
        catch (Exception)
        {
            resend.RecordSendFailure("The provider did not accept the re-send.");
            _logger.LogWarning("Failed to re-send notification {NotificationId}", original.Id);
        }

        await _notifications.UpdateAsync(resend, cancellationToken);
        return resend;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
                           ?? throw new NotificationNotFoundException(notificationId);

        if (!string.IsNullOrWhiteSpace(notification.ProviderSid))
        {
            try
            {
                await _sms.RedactBodyAsync(notification.ProviderSid, cancellationToken);
            }
            catch (Exception)
            {
                _logger.LogWarning("Provider redaction call failed for notification {NotificationId}", notificationId);
            }
        }

        RedactedIds[notificationId] = 1;
        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
        await _persistence.MarkContentRedactedAsync(notificationId, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken)
    {
        var local = await _notifications.ListAsync(new NotificationsInCreatedRangeSpec(fromUtc, toUtc), cancellationToken);
        var localBySid = local
            .Where(n => !string.IsNullOrWhiteSpace(n.ProviderSid))
            .GroupBy(n => n.ProviderSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var providerMessages = new List<SmsMessageSnapshot>();
        string? pageToken = null;
        var pages = 0;
        var truncated = false;
        const int maxPages = 50;

        do
        {
            var page = await _sms.ListSentFromConfiguredNumberAsync(fromUtc, toUtc, pageToken, cancellationToken);
            if (page.Messages.Count > 0)
                providerMessages.AddRange(page.Messages);

            pages++;
            if (pages >= maxPages && page.HasMore)
            {
                truncated = true;
                break;
            }

            pageToken = page.NextPageToken;
            if (!page.HasMore || string.IsNullOrWhiteSpace(pageToken))
                break;
        } while (true);

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrWhiteSpace(m.Sid))
            .GroupBy(m => m.Sid, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matched = new List<ReconciliationRow>();
        var providerOnly = new List<ReconciliationRow>();
        var eshopOnly = new List<ReconciliationRow>();

        foreach (var pair in providerBySid)
        {
            if (localBySid.TryGetValue(pair.Key, out var localNote))
            {
                matched.Add(new ReconciliationRow(localNote.Id.ToString(), pair.Key, pair.Value.Status ?? localNote.Status, "both"));
            }
            else
            {
                providerOnly.Add(new ReconciliationRow(null, pair.Key, pair.Value.Status, "provider"));
            }
        }

        foreach (var localNote in local)
        {
            if (string.IsNullOrWhiteSpace(localNote.ProviderSid))
            {
                eshopOnly.Add(new ReconciliationRow(localNote.Id.ToString(), null, localNote.Status, "eshop"));
                continue;
            }

            if (!providerBySid.ContainsKey(localNote.ProviderSid))
                eshopOnly.Add(new ReconciliationRow(localNote.Id.ToString(), localNote.ProviderSid, localNote.Status, "eshop"));
        }

        return new ReconciliationReport(fromUtc, toUtc, truncated, matched, providerOnly, eshopOnly);
    }

    private async Task TrySendImmediateAsync(Order order, string kind, string body, CancellationToken cancellationToken)
    {
        var destination = await GetPrimaryDestinationAsync(order.BuyerId, cancellationToken);
        if (destination is null)
        {
            _logger.LogInformation("No contact number on file; skipping {Kind} for order {OrderId}", kind, order.Id);
            return;
        }

        var notification = new OrderNotification(order.Id, order.BuyerId, kind, body, destination);
        notification = await _notifications.AddAsync(notification, cancellationToken);

        try
        {
            var result = await _sms.SendImmediateAsync(destination, body, cancellationToken);
            ApplyDispatchResult(notification, result);
        }
        catch (Exception)
        {
            notification.RecordSendFailure("The provider did not accept the message.");
            _logger.LogWarning("Failed to send {Kind} notification for order {OrderId}", kind, order.Id);
        }

        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    private async Task CancelFollowUpsAsync(IReadOnlyList<OrderNotification> followUps, CancellationToken cancellationToken)
    {
        foreach (var followUp in followUps)
        {
            await RefreshFromProviderAsync(new[] { followUp }, cancellationToken);
            if (!followUp.CanBeCancelledAtProvider() || string.IsNullOrWhiteSpace(followUp.ProviderSid))
                continue;

            try
            {
                var result = await _sms.CancelScheduledAsync(followUp.ProviderSid, cancellationToken);
                ApplyDispatchResult(followUp, result);
            }
            catch (Exception)
            {
                _logger.LogWarning("Could not cancel scheduled follow-up {NotificationId}; it may already have been sent", followUp.Id);
            }

            await _notifications.UpdateAsync(followUp, cancellationToken);
        }
    }

    private async Task RefreshFromProviderAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrWhiteSpace(notification.ProviderSid) || notification.ContentRedacted || RedactedIds.ContainsKey(notification.Id))
                continue;
            if (notification.IsTerminalStatus())
                continue;

            try
            {
                var snapshot = await _sms.FetchAsync(notification.ProviderSid, cancellationToken);
                if (snapshot is null)
                    continue;

                notification.ApplyProviderSnapshot(snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage, body: null);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception)
            {
                _logger.LogWarning("Could not refresh provider status for notification {NotificationId}", notification.Id);
            }
        }
    }

    private static void ApplyLocalRedaction(IEnumerable<OrderNotification> notifications)
    {
        foreach (var notification in notifications)
        {
            if (RedactedIds.ContainsKey(notification.Id))
                notification.MarkContentRedacted();
        }
    }

    private static void ApplyDispatchResult(OrderNotification notification, SmsDispatchResult result)
    {
        if (result.ReachedProvider)
            notification.RecordProviderAcceptance(result.ProviderSid, result.Status, result.ErrorCode, result.ErrorMessage);
        else
            notification.RecordSendFailure(result.ErrorMessage ?? "The provider did not accept the message.");
    }

    private async Task<string?> GetPrimaryDestinationAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpec(buyerId), cancellationToken);
        return numbers.FirstOrDefault()?.CanonicalNumber;
    }

    private async Task<string?> GetRegisteredDestinationAsync(string buyerId, string canonical, CancellationToken cancellationToken)
    {
        var match = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpec(buyerId, canonical), cancellationToken);
        return match?.CanonicalNumber;
    }
}
