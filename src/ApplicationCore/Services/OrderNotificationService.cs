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
    internal static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<NotificationResendKey> _resendKeys;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ITwilioMessagingService _messaging;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notifications,
        IRepository<NotificationResendKey> resendKeys,
        IRepository<ContactNumber> contactNumbers,
        ITwilioMessagingService messaging,
        IAppLogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _resendKeys = resendKeys;
        _contactNumbers = contactNumbers;
        _messaging = messaging;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
        => NotifyAsync(order, OrderNotificationKind.OrderPlaced, BuildBody(OrderNotificationKind.OrderPlaced, order.Id), sendAt: null, cancellationToken);

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await NotifyAsync(order, OrderNotificationKind.OrderDispatched, BuildBody(OrderNotificationKind.OrderDispatched, order.Id), sendAt: null, cancellationToken);
        var followUpAt = DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay);
        await NotifyAsync(order, OrderNotificationKind.DeliveryFollowUp, BuildBody(OrderNotificationKind.DeliveryFollowUp, order.Id), followUpAt, cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);
        await NotifyAsync(order, OrderNotificationKind.OrderCancelled, BuildBody(OrderNotificationKind.OrderCancelled, order.Id), sendAt: null, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> GetForOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var items = await _notifications.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        await SyncFromProviderAsync(items, cancellationToken);
        return items;
    }

    public async Task<IReadOnlyList<OrderNotification>> GetForBuyerOrdersAsync(IReadOnlyList<int> orderIds, CancellationToken cancellationToken = default)
    {
        if (orderIds.Count == 0)
        {
            return Array.Empty<OrderNotification>();
        }

        var items = await _notifications.ListAsync(new NotificationsByOrdersSpecification(orderIds), cancellationToken);
        await SyncFromProviderAsync(items, cancellationToken);
        return items;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var existingKey = await _resendKeys.FirstOrDefaultAsync(
            new ResendKeySpecification(notificationId, idempotencyKey), cancellationToken);
        if (existingKey is not null)
        {
            var existing = await _notifications.GetByIdAsync(existingKey.ResultNotificationId, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotificationNotFoundException(notificationId);

        await RefreshOneAsync(original, cancellationToken);

        if (!original.DidNotReachShopper() && original.ProviderSid is not null)
        {
            throw new NotificationStateException("Only messages that did not reach the shopper can be resent.");
        }

        var destination = await ResolveActiveDestinationAsync(original.BuyerId, original.DestinationNumber, cancellationToken);
        if (destination is null)
        {
            throw new NotificationStateException("The destination number is no longer on file for this shopper.");
        }

        var body = original.ContentRedacted || string.IsNullOrEmpty(original.Body)
            ? BuildBody(original.Kind, original.OrderId)
            : original.Body;

        var resent = new OrderNotification(original.OrderId, original.BuyerId, original.Kind, destination, body, original.Id);
        await SendAndPersistAsync(resent, sendAt: null, cancellationToken);

        var key = new NotificationResendKey(notificationId, idempotencyKey, resent.Id);
        await _resendKeys.AddAsync(key, cancellationToken);
        return resent;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotificationNotFoundException(notificationId);

        if (!string.IsNullOrEmpty(notification.ProviderSid))
        {
            try
            {
                await _messaging.RedactBodyAsync(notification.ProviderSid, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Failed to redact provider content for notification {NotificationId}: {Reason}", notification.Id, SafeReason(ex));
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

        var fromNumber = await GetConfiguredFromNumberAsync();
        var providerMessages = await _messaging.ListFromAsync(fromNumber, from, to, cancellationToken);
        var local = await _notifications.ListAsync(new NotificationsWithProviderSidSpecification(), cancellationToken);

        var localBySid = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderSid))
            .GroupBy(n => n.ProviderSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var providerBySid = providerMessages
            .GroupBy(m => m.Sid, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var rows = new List<ReconciliationRow>();

        foreach (var provider in providerMessages)
        {
            if (localBySid.TryGetValue(provider.Sid, out var notification))
            {
                rows.Add(new ReconciliationRow(provider.Sid, notification.Id, "matched", provider.Status, notification.ProviderStatus, provider.DateSent));
            }
            else
            {
                rows.Add(new ReconciliationRow(provider.Sid, null, "providerOnly", provider.Status, null, provider.DateSent));
            }
        }

        foreach (var notification in local)
        {
            if (string.IsNullOrEmpty(notification.ProviderSid))
            {
                continue;
            }

            if (!providerBySid.ContainsKey(notification.ProviderSid))
            {
                var created = notification.CreatedAt;
                var scheduled = notification.ScheduledSendAt;
                var inRange = (created >= from && created <= to) || (scheduled is not null && scheduled >= from && scheduled <= to);
                if (inRange)
                {
                    rows.Add(new ReconciliationRow(notification.ProviderSid, notification.Id, "applicationOnly", null, notification.ProviderStatus, null));
                }
            }
        }

        return new ReconciliationReport(from, to, fromNumber, rows);
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
            var destination = await GetLatestDestinationAsync(order.BuyerId, cancellationToken);
            if (destination is null)
            {
                _logger.LogInformation("Skipping {Kind} SMS for order {OrderId}; shopper has no number on file.", kind, order.Id);
                return;
            }

            var notification = new OrderNotification(order.Id, order.BuyerId, kind, destination, body);
            await SendAndPersistAsync(notification, sendAt, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("SMS notification {Kind} for order {OrderId} did not send: {Reason}", kind, order.Id, SafeReason(ex));
        }
    }

    private async Task SendAndPersistAsync(OrderNotification notification, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        try
        {
            var sent = await _messaging.SendAsync(new SendMessageRequest(notification.DestinationNumber, notification.Body ?? string.Empty, sendAt), cancellationToken);
            notification.RecordAccepted(sent.Sid, sent.Status ?? "queued", sendAt);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            notification.RecordSendFailure(SafeReason(ex));
            _logger.LogWarning("Provider rejected or failed SMS for notification kind {Kind} on order {OrderId}: {Reason}", notification.Kind, notification.OrderId, SafeReason(ex));
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
        var pending = await _notifications.ListAsync(new ScheduledFollowUpsByOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in pending)
        {
            if (string.IsNullOrEmpty(followUp.ProviderSid))
            {
                continue;
            }

            try
            {
                var updated = await _messaging.CancelAsync(followUp.ProviderSid, cancellationToken);
                followUp.SyncFromProvider(updated.Status ?? "canceled", updated.ErrorCode);
                await _notifications.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Failed to cancel scheduled follow-up {NotificationId} for order {OrderId}: {Reason}", followUp.Id, orderId, SafeReason(ex));
            }
        }
    }

    private async Task SyncFromProviderAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            await RefreshOneAsync(notification, cancellationToken);
        }
    }

    private async Task RefreshOneAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(notification.ProviderSid))
        {
            return;
        }

        try
        {
            var current = await _messaging.FetchAsync(notification.ProviderSid, cancellationToken);
            var status = current.Status ?? notification.ProviderStatus ?? "unknown";
            notification.SyncFromProvider(status, current.ErrorCode);
            if (!notification.ContentRedacted && string.IsNullOrEmpty(current.Body))
            {
                notification.RedactContent();
            }

            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Could not refresh provider status for notification {NotificationId}: {Reason}", notification.Id, SafeReason(ex));
        }
    }

    private async Task<string?> GetLatestDestinationAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers.FirstOrDefault()?.PhoneNumber;
    }

    private async Task<string?> ResolveActiveDestinationAsync(string buyerId, string originalDestination, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        var match = numbers.FirstOrDefault(n => string.Equals(n.PhoneNumber, originalDestination, StringComparison.Ordinal));
        return match?.PhoneNumber;
    }

    private static string BuildBody(OrderNotificationKind kind, int orderId) => kind switch
    {
        OrderNotificationKind.OrderPlaced => $"Your eShop order #{orderId} has been placed. Thank you for your purchase.",
        OrderNotificationKind.OrderDispatched => $"Your eShop order #{orderId} is on its way.",
        OrderNotificationKind.DeliveryFollowUp => $"How did delivery go for eShop order #{orderId}?",
        OrderNotificationKind.OrderCancelled => $"Your eShop order #{orderId} has been cancelled.",
        _ => $"An update is available for eShop order #{orderId}."
    };

    private Task<string> GetConfiguredFromNumberAsync()
    {
        if (string.IsNullOrWhiteSpace(_messaging.FromNumber))
        {
            throw new InvalidOperationException("Twilio:FromNumber is not configured.");
        }

        return Task.FromResult(_messaging.FromNumber);
    }

    private static string SafeReason(Exception ex)
    {
        if (ex is TwilioMessagingException twilio)
        {
            return $"Twilio error {twilio.ErrorCode}";
        }

        return ex.GetType().Name;
    }
}
