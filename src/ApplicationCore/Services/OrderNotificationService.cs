using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<NotificationResendIdempotency> _idempotency;
    private readonly ISmsNotificationGateway _smsGateway;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<ShopperContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IRepository<NotificationResendIdempotency> idempotency,
        ISmsNotificationGateway smsGateway,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _idempotency = idempotency;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken)
        => NotifyImmediateAsync(
            order,
            OrderNotificationKind.OrderPlaced,
            $"Your eShop order #{order.Id} has been placed.",
            cancellationToken);

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken)
    {
        await NotifyImmediateAsync(
            order,
            OrderNotificationKind.OrderDispatched,
            $"Your eShop order #{order.Id} is on its way.",
            cancellationToken);

        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        await NotifyScheduledAsync(
            order,
            OrderNotificationKind.DeliveryFollowUp,
            $"How did the delivery of eShop order #{order.Id} go?",
            sendAt,
            cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken)
    {
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);
        await NotifyImmediateAsync(
            order,
            OrderNotificationKind.OrderCancelled,
            $"Your eShop order #{order.Id} has been cancelled.",
            cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var list = await _notifications.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshFromProviderAsync(list, cancellationToken);
        return list;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken)
    {
        var list = await _notifications.ListAsync(new NotificationsByBuyerSpecification(buyerId), cancellationToken);
        await RefreshFromProviderAsync(list, cancellationToken);
        return list;
    }

    public async Task RefreshFromProviderAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderSid))
            {
                continue;
            }

            var fetched = await _smsGateway.FetchAsync(notification.ProviderSid, cancellationToken);
            if (fetched is null)
            {
                continue;
            }

            ApplyProviderResult(notification, fetched);
            if (fetched.Body is null || fetched.Body.Length == 0)
            {
                notification.RedactContent();
            }

            await _notifications.UpdateAsync(notification, cancellationToken);
        }
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        var existing = await _idempotency.FirstOrDefaultAsync(
            new ResendIdempotencySpecification(notificationId, idempotencyKey),
            cancellationToken);
        if (existing is not null)
        {
            var prior = await _notifications.GetByIdAsync(existing.ResultNotificationId, cancellationToken);
            if (prior is not null)
            {
                return prior;
            }
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException("Notification was not found.");

        if (!string.IsNullOrEmpty(original.ProviderSid))
        {
            var current = await _smsGateway.FetchAsync(original.ProviderSid, cancellationToken);
            if (current is not null)
            {
                ApplyProviderResult(original, current);
                await _notifications.UpdateAsync(original, cancellationToken);
            }
        }

        if (!original.DidNotReachShopper())
        {
            throw new InvalidOperationException("Only messages that did not reach the shopper can be re-sent.");
        }

        if (original.ContentRedacted || string.IsNullOrEmpty(original.Body))
        {
            throw new InvalidOperationException("The original message content is no longer available to re-send.");
        }

        var numbers = await ResolveDestinationsAsync(original.BuyerId, cancellationToken);
        var destination = original.DestinationNumber
            ?? numbers.FirstOrDefault()?.CanonicalNumber;
        if (string.IsNullOrEmpty(destination))
        {
            throw new InvalidOperationException("The shopper has no contact number on file.");
        }

        var resent = new OrderNotification(original.OrderId, original.BuyerId, original.Kind, original.Body, destination);
        resent.MarkResentFrom(original.Id);
        resent = await _notifications.AddAsync(resent, cancellationToken);

        var sent = await _smsGateway.SendImmediateAsync(destination, original.Body, cancellationToken);
        ApplySendResult(resent, sent);
        await _notifications.UpdateAsync(resent, cancellationToken);

        await _idempotency.AddAsync(
            new NotificationResendIdempotency(notificationId, idempotencyKey, resent.Id),
            cancellationToken);

        return resent;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException("Notification was not found.");

        if (!string.IsNullOrEmpty(notification.ProviderSid))
        {
            var redacted = await _smsGateway.RedactBodyAsync(notification.ProviderSid, cancellationToken);
            if (redacted is null)
            {
                throw new Exceptions.MessagingProviderException(
                    "The provider could not dispose of the message content.");
            }

            ApplyProviderResult(notification, redacted);
            if (redacted.Body is not null && redacted.Body.Length > 0)
            {
                throw new Exceptions.MessagingProviderException(
                    "The provider still returned message content after disposal.");
            }
        }

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var providerMessages = await _smsGateway.ListFromSenderAsync(from, to, cancellationToken);
        var truncated = providerMessages.Count >= 20 * 1000;

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid!)
            .ToDictionary(g => g.Key, g => g.First());

        var local = await _notifications.ListAsync(
            new NotificationsCreatedInRangeSpecification(from, to),
            cancellationToken);

        var localBySid = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderSid))
            .GroupBy(n => n.ProviderSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var extraLocalWithSid = Array.Empty<OrderNotification>();
        var missingSids = providerBySid.Keys.Except(localBySid.Keys).ToArray();
        if (missingSids.Length > 0)
        {
            extraLocalWithSid = (await _notifications.ListAsync(
                new NotificationsByProviderSidsSpecification(missingSids),
                cancellationToken)).ToArray();
            foreach (var n in extraLocalWithSid)
            {
                localBySid[n.ProviderSid!] = n;
            }
        }

        var matched = new List<ReconciledNotification>();
        var providerOnly = new List<ReconciledNotification>();
        var applicationOnly = new List<ReconciledNotification>();

        foreach (var (sid, provider) in providerBySid)
        {
            if (localBySid.TryGetValue(sid, out var localMatch))
            {
                matched.Add(ToReconciled(localMatch, provider, "matched"));
            }
            else
            {
                providerOnly.Add(new ReconciledNotification(
                    null,
                    provider.Sid,
                    provider.Status,
                    BodyOrNull(provider.Body),
                    provider.DateSent,
                    provider.ErrorCode,
                    "provider"));
            }
        }

        foreach (var notification in local.Concat(extraLocalWithSid).DistinctBy(n => n.Id))
        {
            if (string.IsNullOrEmpty(notification.ProviderSid) || !providerBySid.ContainsKey(notification.ProviderSid))
            {
                applicationOnly.Add(ToReconciled(notification, null, "application"));
            }
        }

        return new NotificationReconciliationReport(
            from,
            to,
            _smsGateway.FromNumber,
            truncated,
            matched,
            providerOnly,
            applicationOnly);
    }

    private async Task NotifyImmediateAsync(
        Order order,
        OrderNotificationKind kind,
        string body,
        CancellationToken cancellationToken)
    {
        var destinations = await ResolveDestinationsAsync(order.BuyerId, cancellationToken);
        if (destinations.Count == 0)
        {
            return;
        }

        foreach (var destination in destinations)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, kind, body, destination.CanonicalNumber);
            notification = await _notifications.AddAsync(notification, cancellationToken);

            var sent = await _smsGateway.SendImmediateAsync(destination.CanonicalNumber, body, cancellationToken);
            ApplySendResult(notification, sent);
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
    }

    private async Task NotifyScheduledAsync(
        Order order,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset sendAt,
        CancellationToken cancellationToken)
    {
        var destinations = await ResolveDestinationsAsync(order.BuyerId, cancellationToken);
        if (destinations.Count == 0)
        {
            return;
        }

        foreach (var destination in destinations)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, kind, body, destination.CanonicalNumber);
            notification = await _notifications.AddAsync(notification, cancellationToken);

            var sent = await _smsGateway.ScheduleAsync(destination.CanonicalNumber, body, sendAt, cancellationToken);
            ApplySendResult(notification, sent, sendAt);
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var pending = await _notifications.ListAsync(new PendingFollowUpNotificationsSpecification(orderId), cancellationToken);
        foreach (var followUp in pending)
        {
            if (string.IsNullOrEmpty(followUp.ProviderSid))
            {
                followUp.ApplyProviderState("canceled", null, null, followUp.ProviderFrom, followUp.ProviderDateCreated, followUp.ProviderDateSent);
                await _notifications.UpdateAsync(followUp, cancellationToken);
                continue;
            }

            var cancelled = await _smsGateway.CancelScheduledAsync(followUp.ProviderSid, cancellationToken);
            if (cancelled is not null)
            {
                ApplyProviderResult(followUp, cancelled);
            }
            else
            {
                _logger.LogWarning("Could not cancel scheduled follow-up {NotificationId} for order {OrderId}.", followUp.Id, orderId);
            }

            await _notifications.UpdateAsync(followUp, cancellationToken);
        }
    }

    private async Task<IReadOnlyList<ShopperContactNumber>> ResolveDestinationsAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
    }

    private void ApplySendResult(OrderNotification notification, ProviderMessageResult? sent, DateTimeOffset? scheduledSendAt = null)
    {
        if (sent is null || string.IsNullOrEmpty(sent.Sid))
        {
            notification.RecordLocalSendFailure("The provider did not accept the message.");
            return;
        }

        notification.RecordProviderAcceptance(
            sent.Sid,
            sent.Status ?? "queued",
            sent.From,
            sent.DateCreated,
            sent.DateSent,
            scheduledSendAt);
        if (sent.ErrorCode is not null)
        {
            notification.ApplyProviderState(
                sent.Status ?? notification.DeliveryStatus,
                sent.ErrorCode,
                sent.ErrorMessage,
                sent.From,
                sent.DateCreated,
                sent.DateSent);
        }
    }

    private static void ApplyProviderResult(OrderNotification notification, ProviderMessageResult fetched)
    {
        notification.ApplyProviderState(
            fetched.Status ?? notification.DeliveryStatus,
            fetched.ErrorCode,
            fetched.ErrorMessage,
            fetched.From,
            fetched.DateCreated,
            fetched.DateSent);
    }

    private static ReconciledNotification ToReconciled(
        OrderNotification local,
        ProviderMessageResult? provider,
        string source)
    {
        return new ReconciledNotification(
            local.Id,
            provider?.Sid ?? local.ProviderSid,
            provider?.Status ?? local.DeliveryStatus,
            local.ContentRedacted ? null : BodyOrNull(provider?.Body ?? local.Body),
            provider?.DateSent ?? local.ProviderDateSent,
            provider?.ErrorCode ?? local.ErrorCode,
            source);
    }

    private static string? BodyOrNull(string? body)
        => string.IsNullOrEmpty(body) ? null : body;
}
