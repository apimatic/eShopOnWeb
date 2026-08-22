using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<NotificationResendRecord> _resendRepository;
    private readonly IContactNumberService _contactNumberService;
    private readonly IMessagingProvider _messagingProvider;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notificationRepository,
        IRepository<NotificationResendRecord> resendRepository,
        IContactNumberService contactNumberService,
        IMessagingProvider messagingProvider,
        IAppLogger<OrderNotificationService> logger)
    {
        _notificationRepository = notificationRepository;
        _resendRepository = resendRepository;
        _contactNumberService = contactNumberService;
        _messagingProvider = messagingProvider;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(int orderId, string buyerId, decimal total, CancellationToken ct) =>
        SendImmediateAsync(orderId, buyerId, OrderNotificationKind.OrderPlaced, total, ct);

    public async Task NotifyOrderDispatchedAsync(int orderId, string buyerId, CancellationToken ct)
    {
        await SendImmediateAsync(orderId, buyerId, OrderNotificationKind.OrderDispatched, 0m, ct);
        await ScheduleFollowUpAsync(orderId, buyerId, ct);
    }

    public Task NotifyOrderCancelledAsync(int orderId, string buyerId, CancellationToken ct) =>
        SendImmediateAsync(orderId, buyerId, OrderNotificationKind.OrderCancelled, 0m, ct);

    public async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken ct)
    {
        var pending = await _notificationRepository.ListAsync(new PendingFollowUpsByOrderSpecification(orderId), ct);
        foreach (var notification in pending)
        {
            await CancelScheduledAsync(notification, ct);
        }
    }

    public async Task CancelPendingFollowUpsToNumberAsync(string canonicalNumber, CancellationToken ct)
    {
        var pending = await _notificationRepository.ListAsync(
            new PendingFollowUpsByNumberSpecification(canonicalNumber), ct);
        foreach (var notification in pending)
        {
            await CancelScheduledAsync(notification, ct);
        }
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, CancellationToken ct)
    {
        return await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), ct);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForBuyerAsync(string buyerId, CancellationToken ct)
    {
        return await _notificationRepository.ListAsync(new OrderNotificationsByBuyerSpecification(buyerId), ct);
    }

    public async Task RefreshFromProviderAsync(IEnumerable<OrderNotification> notifications, CancellationToken ct)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderSid))
            {
                continue;
            }

            try
            {
                var latest = await _messagingProvider.FetchAsync(notification.ProviderSid, ct);
                ApplyProvider(notification, latest);
                await _notificationRepository.UpdateAsync(notification, ct);
            }
            catch (Exception)
            {
                _logger.LogWarning("Could not refresh provider status for notification {NotificationId}.", notification.Id);
            }
        }
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new OrderStateException("An idempotency key is required.");
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, ct)
            ?? throw new EntityNotFoundException("Notification");

        var existing = await _resendRepository.FirstOrDefaultAsync(
            new ResendRecordByKeySpecification(notificationId, idempotencyKey.Trim()), ct);
        if (existing != null)
        {
            var previous = await _notificationRepository.GetByIdAsync(existing.ResultNotificationId, ct);
            if (previous != null)
            {
                return previous;
            }
        }

        var destination = original.DestinationCanonical;
        if (string.IsNullOrEmpty(destination))
        {
            var preferred = await _contactNumberService.GetPreferredAsync(original.BuyerId, ct);
            destination = preferred?.CanonicalNumber;
        }

        var body = original.ResolveBodyForResend();
        var resent = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            OrderNotificationKind.Resend,
            destination,
            body);
        resent.MarkResentFrom(original.Id);
        resent = await _notificationRepository.AddAsync(resent, ct);

        if (string.IsNullOrEmpty(destination))
        {
            resent.RecordSendFailure("No destination number is on file.");
            await _notificationRepository.UpdateAsync(resent, ct);
        }
        else
        {
            await TrySendAsync(resent, () => _messagingProvider.SendAsync(destination, body, ct), ct);
        }

        var record = new NotificationResendRecord(original.Id, idempotencyKey.Trim(), resent.Id);
        await _resendRepository.AddAsync(record, ct);
        return resent;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken ct)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, ct)
            ?? throw new EntityNotFoundException("Notification");

        if (!string.IsNullOrEmpty(notification.ProviderSid))
        {
            var updated = await _messagingProvider.RedactBodyAsync(notification.ProviderSid, ct);
            ApplyProvider(notification, updated);
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, ct);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var providerMessages = await _messagingProvider.ListSentFromConfiguredNumberAsync(from, to, ct);
        var local = await _notificationRepository.ListAsync(new OrderNotificationsWithProviderSidSpecification(), ct);
        var localBySid = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderSid))
            .GroupBy(n => n.ProviderSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var message in providerMessages)
        {
            if (string.IsNullOrEmpty(message.Sid))
            {
                continue;
            }

            seen.Add(message.Sid);
            if (localBySid.TryGetValue(message.Sid, out var notification))
            {
                matched.Add(ToEntry(notification, message));
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry
                {
                    ProviderSid = message.Sid,
                    ProviderStatus = message.Status,
                    DateSent = message.DateSent,
                    DateCreated = message.DateCreated
                });
            }
        }

        var eshopOnly = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderSid) && !seen.Contains(n.ProviderSid!))
            .Select(n => new ReconciliationEntry
            {
                NotificationId = n.Id,
                ProviderSid = n.ProviderSid,
                EshopStatus = n.ProviderStatus,
                OrderId = n.OrderId
            })
            .ToList();

        return new ReconciliationReport
        {
            From = from,
            To = to,
            Matched = matched,
            ProviderOnly = providerOnly,
            EshopOnly = eshopOnly
        };
    }

    private async Task SendImmediateAsync(
        int orderId,
        string buyerId,
        OrderNotificationKind kind,
        decimal total,
        CancellationToken ct)
    {
        var preferred = await _contactNumberService.GetPreferredAsync(buyerId, ct);
        if (preferred == null)
        {
            _logger.LogInformation("No contact number on file for order {OrderId}; skipping {Kind}.", orderId, kind);
            return;
        }

        var body = OrderNotificationMessages.ForKind(kind, orderId, total);
        var notification = new OrderNotification(orderId, buyerId, kind, preferred.CanonicalNumber, body);
        notification = await _notificationRepository.AddAsync(notification, ct);
        await TrySendAsync(notification, () => _messagingProvider.SendAsync(preferred.CanonicalNumber, body, ct), ct);
    }

    private async Task ScheduleFollowUpAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var preferred = await _contactNumberService.GetPreferredAsync(buyerId, ct);
        if (preferred == null)
        {
            return;
        }

        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var body = OrderNotificationMessages.ForKind(OrderNotificationKind.DeliveryFollowUp, orderId, 0m);
        var notification = new OrderNotification(
            orderId,
            buyerId,
            OrderNotificationKind.DeliveryFollowUp,
            preferred.CanonicalNumber,
            body);
        notification = await _notificationRepository.AddAsync(notification, ct);
        await TrySendAsync(
            notification,
            () => _messagingProvider.ScheduleAsync(preferred.CanonicalNumber, body, sendAt, ct),
            ct,
            sendAt);
    }

    private async Task CancelScheduledAsync(OrderNotification notification, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(notification.ProviderSid))
        {
            return;
        }

        try
        {
            var updated = await _messagingProvider.CancelScheduledAsync(notification.ProviderSid, ct);
            ApplyProvider(notification, updated);
            await _notificationRepository.UpdateAsync(notification, ct);
        }
        catch (Exception)
        {
            _logger.LogWarning("Could not cancel scheduled notification {NotificationId}; fetching current status.", notification.Id);
            try
            {
                var latest = await _messagingProvider.FetchAsync(notification.ProviderSid, ct);
                ApplyProvider(notification, latest);
                await _notificationRepository.UpdateAsync(notification, ct);
            }
            catch (Exception)
            {
                _logger.LogWarning("Could not fetch scheduled notification {NotificationId} after cancel failure.", notification.Id);
            }
        }
    }

    private async Task TrySendAsync(
        OrderNotification notification,
        Func<Task<ProviderMessage>> send,
        CancellationToken ct,
        DateTimeOffset? sendAt = null)
    {
        try
        {
            var result = await send();
            ApplyProvider(notification, result, sendAt);
        }
        catch (Exception)
        {
            notification.RecordSendFailure("The messaging provider did not accept the message.");
            _logger.LogWarning("Messaging failed for notification {NotificationId} on order {OrderId}.", notification.Id, notification.OrderId);
        }

        await _notificationRepository.UpdateAsync(notification, ct);
    }

    private static void ApplyProvider(OrderNotification notification, ProviderMessage result, DateTimeOffset? sendAt = null)
    {
        notification.RecordProviderResult(
            result.Sid,
            result.Status,
            result.ErrorCode,
            result.ErrorMessage,
            result.Body,
            sendAt);
    }

    private static ReconciliationEntry ToEntry(OrderNotification notification, ProviderMessage message) => new()
    {
        NotificationId = notification.Id,
        ProviderSid = message.Sid,
        ProviderStatus = message.Status,
        EshopStatus = notification.ProviderStatus,
        OrderId = notification.OrderId,
        DateSent = message.DateSent,
        DateCreated = message.DateCreated
    };
}
