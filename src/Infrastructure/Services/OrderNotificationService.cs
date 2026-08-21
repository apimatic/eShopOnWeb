using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly ITwilioMessagingClient _messagingClient;
    private readonly IContactNumberService _contactNumberService;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly TwilioSettings _settings;
    private readonly ILogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        ITwilioMessagingClient messagingClient,
        IContactNumberService contactNumberService,
        IRepository<OrderNotification> notificationRepository,
        IOptions<TwilioSettings> options,
        ILogger<OrderNotificationService> logger)
    {
        _messagingClient = messagingClient;
        _contactNumberService = contactNumberService;
        _notificationRepository = notificationRepository;
        _settings = options.Value;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default) =>
        TrySendAsync(order, OrderNotificationKind.OrderPlaced, BuildBody(OrderNotificationKind.OrderPlaced, order.Id), scheduledSendAt: null, cancellationToken);

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await TrySendAsync(order, OrderNotificationKind.OrderDispatched, BuildBody(OrderNotificationKind.OrderDispatched, order.Id), scheduledSendAt: null, cancellationToken);
        await TrySendAsync(
            order,
            OrderNotificationKind.DeliveryFollowUp,
            BuildBody(OrderNotificationKind.DeliveryFollowUp, order.Id),
            DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay),
            cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);
        await TrySendAsync(order, OrderNotificationKind.OrderCancelled, BuildBody(OrderNotificationKind.OrderCancelled, order.Id), scheduledSendAt: null, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, bool refreshFromProvider, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderIdSpecification(orderId), cancellationToken);
        if (refreshFromProvider)
        {
            await RefreshAsync(notifications, cancellationToken);
        }

        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForBuyerAsync(string buyerId, bool refreshFromProvider, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.ListAsync(new NotificationsByBuyerSpecification(buyerId), cancellationToken);
        if (refreshFromProvider)
        {
            await RefreshAsync(notifications, cancellationToken);
        }

        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new NotificationByResendIdempotencySpecification(notificationId, idempotencyKey),
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var original = await _notificationRepository.FirstOrDefaultAsync(
            new NotificationByIdSpecification(notificationId),
            cancellationToken);
        if (original is null)
        {
            throw new KeyNotFoundException("Notification not found.");
        }

        var body = original.ContentRedacted || string.IsNullOrEmpty(original.Body)
            ? BuildBody(original.Kind == OrderNotificationKind.Resend ? OrderNotificationKind.OrderPlaced : original.Kind, original.OrderId)
            : original.Body;

        var destination = await _contactNumberService.GetPreferredCanonicalNumberAsync(original.BuyerId, cancellationToken);
        var snapshot = await SendOrCaptureFailureAsync(destination, body, scheduledSendAt: null, cancellationToken);
        var sid = string.IsNullOrEmpty(snapshot.Sid) ? null : snapshot.Sid;

        var resend = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            OrderNotificationKind.Resend,
            body,
            sid,
            snapshot.Status,
            snapshot.ErrorCode,
            snapshot.ErrorMessage,
            snapshot.DateSent,
            scheduledSendAt: null,
            original.Id,
            idempotencyKey);

        return await _notificationRepository.AddAsync(resend, cancellationToken);
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.FirstOrDefaultAsync(
            new NotificationByIdSpecification(notificationId),
            cancellationToken);
        if (notification is null)
        {
            throw new KeyNotFoundException("Notification not found.");
        }

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            var snapshot = await _messagingClient.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
            notification.ApplyProviderState(snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage, snapshot.DateSent, snapshot.Body);
        }

        notification.MarkContentRedacted();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _messagingClient.ListSentFromAsync(_settings.FromNumber, from, to, cancellationToken);
        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First());

        var applicationInRange = await _notificationRepository.ListAsync(
            new NotificationsInCreatedRangeSpecification(from, to),
            cancellationToken);

        var matchedSids = new HashSet<string>(StringComparer.Ordinal);
        var matched = new List<ReconciledNotification>();
        var applicationOnly = new List<OrderNotification>();

        foreach (var notification in applicationInRange)
        {
            if (!string.IsNullOrEmpty(notification.ProviderMessageSid)
                && providerBySid.TryGetValue(notification.ProviderMessageSid, out var provider))
            {
                matchedSids.Add(notification.ProviderMessageSid);
                matched.Add(new ReconciledNotification(notification, provider.Status, provider.ErrorCode));
            }
            else
            {
                applicationOnly.Add(notification);
            }
        }

        var providerOnly = providerBySid.Values
            .Where(m => !matchedSids.Contains(m.Sid))
            .Select(m => new ProviderOnlyMessage(m.Sid, m.Status, m.DateSent, m.DateCreated))
            .ToList();

        return new NotificationReconciliationReport(from, to, _settings.FromNumber, matched, providerOnly, applicationOnly);
    }

    private async Task TrySendAsync(
        Order order,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset? scheduledSendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var destination = await _contactNumberService.GetPreferredCanonicalNumberAsync(order.BuyerId, cancellationToken);
            if (string.IsNullOrEmpty(destination))
            {
                return;
            }

            var snapshot = await SendOrCaptureFailureAsync(destination, body, scheduledSendAt, cancellationToken);
            var sid = string.IsNullOrEmpty(snapshot.Sid) ? null : snapshot.Sid;
            var notification = new OrderNotification(
                order.Id,
                order.BuyerId,
                kind,
                body,
                sid,
                snapshot.Status,
                snapshot.ErrorCode,
                snapshot.ErrorMessage,
                snapshot.DateSent,
                scheduledSendAt);
            await _notificationRepository.AddAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Order {OrderId} notification {Kind} could not be recorded after send.", order.Id, kind);
        }
    }

    private async Task<TwilioMessageSnapshot> SendOrCaptureFailureAsync(
        string? destination,
        string body,
        DateTimeOffset? scheduledSendAt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(destination))
        {
            return new TwilioMessageSnapshot(string.Empty, "skipped", body, null, "No contact number on file.", null, DateTimeOffset.UtcNow, null);
        }

        try
        {
            var snapshot = await _messagingClient.SendAsync(
                new TwilioSendMessageRequest(destination, body, scheduledSendAt),
                cancellationToken);
            return snapshot;
        }
        catch (TwilioApiException ex)
        {
            _logger.LogWarning("Twilio rejected a message with HTTP {StatusCode} and provider code {ProviderCode}", ex.StatusCode, ex.ProviderCode);
            return new TwilioMessageSnapshot(string.Empty, "failed", body, ex.ProviderCode, PhoneNumberSanitizer.Redact(ex.Message), null, DateTimeOffset.UtcNow, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Twilio send failed.");
            return new TwilioMessageSnapshot(string.Empty, "failed", body, null, "The message could not be sent.", null, DateTimeOffset.UtcNow, null);
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notificationRepository.ListAsync(new ScheduledFollowUpsByOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            if (string.IsNullOrEmpty(followUp.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var current = await _messagingClient.FetchAsync(followUp.ProviderMessageSid, cancellationToken);
                followUp.ApplyProviderState(current.Status, current.ErrorCode, current.ErrorMessage, current.DateSent, current.Body);
                if (!string.Equals(current.Status, "scheduled", StringComparison.OrdinalIgnoreCase))
                {
                    await _notificationRepository.UpdateAsync(followUp, cancellationToken);
                    continue;
                }

                var cancelled = await _messagingClient.CancelScheduledAsync(followUp.ProviderMessageSid, cancellationToken);
                followUp.ApplyProviderState(cancelled.Status, cancelled.ErrorCode, cancelled.ErrorMessage, cancelled.DateSent, cancelled.Body);
                await _notificationRepository.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cancel scheduled follow-up for order {OrderId}", orderId);
            }
        }
    }

    private async Task RefreshAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _messagingClient.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                notification.ApplyProviderState(snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage, snapshot.DateSent, snapshot.Body);
                if (notification.ContentRedacted)
                {
                    notification.MarkContentRedacted();
                }
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh provider status for notification {NotificationId}", notification.Id);
            }
        }
    }

    public static string BuildBody(OrderNotificationKind kind, int orderId) => kind switch
    {
        OrderNotificationKind.OrderPlaced => $"Your eShopOnWeb order #{orderId} has been placed.",
        OrderNotificationKind.OrderDispatched => $"Your eShopOnWeb order #{orderId} is on its way.",
        OrderNotificationKind.DeliveryFollowUp => $"How did the delivery of eShopOnWeb order #{orderId} go?",
        OrderNotificationKind.OrderCancelled => $"Your eShopOnWeb order #{orderId} has been cancelled.",
        _ => $"Update for eShopOnWeb order #{orderId}."
    };
}
