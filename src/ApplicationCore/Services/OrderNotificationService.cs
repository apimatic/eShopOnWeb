using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    private static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ResendGates = new();

    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<NotificationResend> _resendRepository;
    private readonly IRepository<Order> _orderRepository;
    private readonly IContactNumberService _contactNumberService;
    private readonly ITwilioMessagingClient _messagingClient;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notificationRepository,
        IRepository<NotificationResend> resendRepository,
        IRepository<Order> orderRepository,
        IContactNumberService contactNumberService,
        ITwilioMessagingClient messagingClient,
        IAppLogger<OrderNotificationService> logger)
    {
        _notificationRepository = notificationRepository;
        _resendRepository = resendRepository;
        _orderRepository = orderRepository;
        _contactNumberService = contactNumberService;
        _messagingClient = messagingClient;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default) =>
        TrySendAsync(order, NotificationKind.OrderPlaced, scheduleAt: null, cancellationToken);

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await TrySendAsync(order, NotificationKind.OrderDispatched, scheduleAt: null, cancellationToken);
        await TrySendAsync(order, NotificationKind.DeliveryFollowUp, DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay), cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);
        await TrySendAsync(order, NotificationKind.OrderCancelled, scheduleAt: null, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderIdSpec(orderId), cancellationToken);
        foreach (var notification in notifications)
        {
            await RefreshFromProviderAsync(notification, cancellationToken);
        }

        return notifications;
    }

    public async Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var source = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (source is null)
        {
            return null;
        }

        await RefreshFromProviderAsync(source, cancellationToken);
        if (!source.DidNotReachShopper())
        {
            throw new InvalidOperationException("Only messages that did not reach the shopper can be resent.");
        }

        var gateKey = $"{source.Id}:{idempotencyKey}";
        var gate = ResendGates.GetOrAdd(gateKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await _resendRepository.FirstOrDefaultAsync(
                new NotificationResendByKeySpec(source.Id, idempotencyKey), cancellationToken);
            if (existing is not null)
            {
                return await _notificationRepository.GetByIdAsync(existing.ResultNotificationId, cancellationToken);
            }

            var order = await _orderRepository.GetByIdAsync(source.OrderId, cancellationToken)
                ?? throw new OrderNotFoundException(source.OrderId);

            var destination = await _contactNumberService.GetActiveDestinationAsync(order.BuyerId, cancellationToken);
            if (destination is null)
            {
                throw new InvalidOperationException("The shopper has no contact number on file.");
            }

            var body = OrderNotificationTemplates.For(source.Kind, source.OrderId);
            var sent = await _messagingClient.SendAsync(destination, body, cancellationToken);
            var produced = new OrderNotification(
                source.OrderId,
                order.BuyerId,
                source.Kind,
                sent.Sid,
                sent.Status,
                body,
                scheduledSendAt: null,
                sourceNotificationId: source.Id,
                idempotencyKey: idempotencyKey);
            produced.ApplyProviderState(sent.Status, sent.ErrorCode, sent.ErrorMessage);
            await _notificationRepository.AddAsync(produced, cancellationToken);
            await _resendRepository.AddAsync(new NotificationResend(source.Id, idempotencyKey, produced.Id), cancellationToken);
            _logger.LogInformation(
                "Resent notification {SourceNotificationId} as {NotificationId} for order {OrderId}.",
                source.Id, produced.Id, source.OrderId);
            return produced;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            await _messagingClient.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Disposed content for notification {NotificationId}.", notificationId);
        return true;
    }

    public async Task<NotificationReconciliationResult> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The 'to' timestamp must be on or after 'from'.");
        }

        var providerMessages = await _messagingClient.ListSentFromConfiguredNumberAsync(from, to, cancellationToken);
        var applicationNotifications = await _notificationRepository.ListAsync(
            new OrderNotificationsByCreatedRangeSpec(from, to), cancellationToken);

        var bySid = applicationNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var matchedSids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var provider in providerMessages)
        {
            if (bySid.TryGetValue(provider.Sid, out var local))
            {
                matchedSids.Add(provider.Sid);
                matched.Add(ToEntry(local, provider));
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry
                {
                    ProviderMessageSid = provider.Sid,
                    ProviderStatus = provider.Status,
                    DateSent = provider.DateSent
                });
            }
        }

        var applicationOnly = applicationNotifications
            .Where(n => string.IsNullOrEmpty(n.ProviderMessageSid) || !matchedSids.Contains(n.ProviderMessageSid!))
            .Select(n => new ReconciliationEntry
            {
                ProviderMessageSid = n.ProviderMessageSid,
                ProviderStatus = n.ProviderStatus,
                NotificationId = n.Id,
                Kind = n.Kind,
                OrderId = n.OrderId
            })
            .ToList();

        return new NotificationReconciliationResult(
            from,
            to,
            _messagingClient.FromNumber,
            matched,
            providerOnly,
            applicationOnly);
    }

    private static ReconciliationEntry ToEntry(OrderNotification local, ProviderMessage provider) =>
        new()
        {
            ProviderMessageSid = provider.Sid,
            ProviderStatus = provider.Status ?? local.ProviderStatus,
            DateSent = provider.DateSent,
            NotificationId = local.Id,
            Kind = local.Kind,
            OrderId = local.OrderId
        };

    private async Task TrySendAsync(
        Order order,
        NotificationKind kind,
        DateTimeOffset? scheduleAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var destination = await _contactNumberService.GetActiveDestinationAsync(order.BuyerId, cancellationToken);
            if (destination is null)
            {
                _logger.LogInformation(
                    "Skipping {Kind} notification for order {OrderId}; no contact number on file.",
                    kind, order.Id);
                return;
            }

            var body = OrderNotificationTemplates.For(kind, order.Id);
            ProviderMessage sent;
            if (scheduleAt is DateTimeOffset when)
            {
                sent = await _messagingClient.ScheduleAsync(destination, body, when, cancellationToken);
            }
            else
            {
                sent = await _messagingClient.SendAsync(destination, body, cancellationToken);
            }

            var notification = new OrderNotification(
                order.Id,
                order.BuyerId,
                kind,
                sent.Sid,
                sent.Status,
                body,
                scheduleAt);
            notification.ApplyProviderState(sent.Status, sent.ErrorCode, sent.ErrorMessage);
            await _notificationRepository.AddAsync(notification, cancellationToken);
            _logger.LogInformation(
                "Recorded {Kind} notification {NotificationId} for order {OrderId} with provider status {Status}.",
                kind, notification.Id, order.Id, sent.Status ?? "unknown");
        }
        catch (Exception)
        {
            _logger.LogWarning("Failed to send {Kind} notification for order {OrderId}.", kind, order.Id);
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var pending = await _notificationRepository.ListAsync(new PendingFollowUpsByOrderIdSpec(orderId), cancellationToken);
        foreach (var notification in pending)
        {
            try
            {
                var updated = await _messagingClient.CancelAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.ApplyProviderState(updated.Status, updated.ErrorCode, updated.ErrorMessage);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
                _logger.LogInformation(
                    "Cancelled scheduled follow-up {NotificationId} for order {OrderId}.",
                    notification.Id, orderId);
            }
            catch (Exception)
            {
                _logger.LogWarning(
                    "Failed to cancel scheduled follow-up {NotificationId} for order {OrderId}.",
                    notification.Id, orderId);
            }
        }
    }

    private async Task RefreshFromProviderAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            return;
        }

        try
        {
            var current = await _messagingClient.FetchAsync(notification.ProviderMessageSid, cancellationToken);
            notification.ApplyProviderState(current.Status, current.ErrorCode, current.ErrorMessage);
            if (string.IsNullOrEmpty(current.Body))
            {
                notification.RedactContent();
            }
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogWarning(
                "Could not refresh provider state for notification {NotificationId}.",
                notification.Id);
        }
    }
}
