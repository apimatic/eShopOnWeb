using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using TwilioSettings = Microsoft.eShopWeb.TwilioSettings;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<NotificationResendRecord> _resendRepository;
    private readonly ITwilioMessageClient _twilio;
    private readonly IShopperContactNumberService _contactNumbers;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notificationRepository,
        IRepository<NotificationResendRecord> resendRepository,
        ITwilioMessageClient twilio,
        IShopperContactNumberService contactNumbers,
        TwilioSettings settings,
        IAppLogger<OrderNotificationService> logger)
    {
        _notificationRepository = notificationRepository;
        _resendRepository = resendRepository;
        _twilio = twilio;
        _contactNumbers = contactNumbers;
        _settings = settings;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
        => SendToLatestNumberAsync(
            order,
            NotificationKind.OrderPlaced,
            $"Your eShopOnWeb order #{order.Id} has been placed. Thank you for your purchase.",
            scheduledAt: null,
            cancellationToken);

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await SendToLatestNumberAsync(
            order,
            NotificationKind.OrderDispatched,
            $"Your eShopOnWeb order #{order.Id} is on its way.",
            scheduledAt: null,
            cancellationToken);

        await SendToLatestNumberAsync(
            order,
            NotificationKind.DeliveryFollowUp,
            $"How did the delivery of your eShopOnWeb order #{order.Id} go?",
            scheduledAt: DateTimeOffset.UtcNow.Add(FollowUpDelay),
            cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        await SendToLatestNumberAsync(
            order,
            NotificationKind.OrderCancelled,
            $"Your eShopOnWeb order #{order.Id} has been cancelled.",
            scheduledAt: null,
            cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> GetForOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderIdSpec(orderId), cancellationToken);
        await RefreshStatusesAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>> GetForOrdersAsync(IReadOnlyCollection<int> orderIds, CancellationToken cancellationToken = default)
    {
        if (orderIds.Count == 0)
        {
            return Array.Empty<OrderNotification>();
        }

        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderIdsSpec(orderIds), cancellationToken);
        await RefreshStatusesAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.");
        }

        var existingReplay = await _resendRepository.FirstOrDefaultAsync(
            new ResendRecordByKeySpec(notificationId, idempotencyKey),
            cancellationToken);
        if (existingReplay != null)
        {
            var replayed = await _notificationRepository.GetByIdAsync(existingReplay.ResultNotificationId, cancellationToken);
            if (replayed != null)
            {
                return replayed;
            }
        }

        var source = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotificationNotFoundException();

        if (source.ContentDisposed || string.IsNullOrWhiteSpace(source.Body))
        {
            throw new InvalidOrderOperationException("The original message content is no longer available to resend.");
        }

        var stillRegistered = await _contactNumbers.IsRegisteredAsync(source.BuyerId, source.DestinationNumber, cancellationToken);
        if (!stillRegistered)
        {
            throw new InvalidOrderOperationException("The destination number is no longer on file and cannot be messaged.");
        }

        var snapshot = await TrySendAsync(source.DestinationNumber, source.Body, scheduledAt: null, cancellationToken);
        var resent = new OrderNotification(
            source.OrderId,
            source.BuyerId,
            source.Kind,
            source.Body,
            source.DestinationNumber,
            snapshot?.Sid,
            snapshot?.Status ?? "failed",
            snapshot?.ErrorCode,
            scheduledAt: null,
            resentFromNotificationId: source.Id);

        resent = await _notificationRepository.AddAsync(resent, cancellationToken);
        await _resendRepository.AddAsync(new NotificationResendRecord(source.Id, idempotencyKey, resent.Id), cancellationToken);
        return resent;
    }

    public async Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotificationNotFoundException();

        if (!string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            var updated = await _twilio.UpdateMessageAsync(
                notification.ProviderMessageSid,
                new TwilioUpdateMessageRequest { Body = string.Empty },
                cancellationToken);

            if (updated == null)
            {
                throw new InvalidOrderOperationException("The provider could not dispose of the message content.");
            }
        }

        notification.MarkContentDisposed();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _twilio.ListMessagesFromAsync(_settings.FromNumber, from, to, cancellationToken);
        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrWhiteSpace(m.Sid))
            .GroupBy(m => m.Sid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var localInRange = await _notificationRepository.ListAsync(new NotificationsInRangeSpec(from, to), cancellationToken);
        var localBySid = new Dictionary<string, OrderNotification>(StringComparer.Ordinal);
        foreach (var local in localInRange.Where(n => !string.IsNullOrWhiteSpace(n.ProviderMessageSid)))
        {
            localBySid[local.ProviderMessageSid!] = local;
        }

        var missingLocalSids = providerBySid.Keys.Where(sid => !localBySid.ContainsKey(sid)).ToList();
        if (missingLocalSids.Count > 0)
        {
            var extraLocal = await _notificationRepository.ListAsync(new NotificationsByProviderSidsSpec(missingLocalSids), cancellationToken);
            foreach (var local in extraLocal.Where(n => !string.IsNullOrWhiteSpace(n.ProviderMessageSid)))
            {
                localBySid.TryAdd(local.ProviderMessageSid!, local);
            }
        }

        var matched = new List<ReconciledMessage>();
        var providerOnly = new List<ReconciledMessage>();
        var eshopOnly = new List<ReconciledMessage>();

        foreach (var (sid, provider) in providerBySid)
        {
            if (localBySid.TryGetValue(sid, out var local))
            {
                matched.Add(ToReconciled(local, provider));
            }
            else
            {
                providerOnly.Add(new ReconciledMessage
                {
                    ProviderSid = provider.Sid,
                    ProviderStatus = provider.Status,
                    Body = provider.Body
                });
            }
        }

        foreach (var local in localInRange)
        {
            if (string.IsNullOrWhiteSpace(local.ProviderMessageSid) || !providerBySid.ContainsKey(local.ProviderMessageSid))
            {
                eshopOnly.Add(ToReconciled(local, provider: null));
            }
        }

        return new NotificationReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = _settings.FromNumber,
            Matched = matched,
            ProviderOnly = providerOnly,
            EshopOnly = eshopOnly
        };
    }

    private async Task SendToLatestNumberAsync(
        Order order,
        string kind,
        string body,
        DateTimeOffset? scheduledAt,
        CancellationToken cancellationToken)
    {
        var destination = await _contactNumbers.GetLatestForBuyerAsync(order.BuyerId, cancellationToken);
        if (destination == null)
        {
            return;
        }

        var snapshot = await TrySendAsync(destination.CanonicalNumber, body, scheduledAt, cancellationToken);
        var notification = new OrderNotification(
            order.Id,
            order.BuyerId,
            kind,
            body,
            destination.CanonicalNumber,
            snapshot?.Sid,
            snapshot?.Status ?? "failed",
            snapshot?.ErrorCode,
            scheduledAt,
            resentFromNotificationId: null);

        await _notificationRepository.AddAsync(notification, cancellationToken);
    }

    private async Task<TwilioMessageSnapshot?> TrySendAsync(
        string destinationNumber,
        string body,
        DateTimeOffset? scheduledAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = new TwilioCreateMessageRequest
            {
                To = destinationNumber,
                Body = body,
                From = _settings.FromNumber,
                MessagingServiceSid = string.IsNullOrWhiteSpace(_settings.MessagingServiceSid) ? null : _settings.MessagingServiceSid,
                ScheduleType = scheduledAt.HasValue ? "fixed" : null,
                SendAt = scheduledAt
            };

            return await _twilio.CreateMessageAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to send order notification: {Message}", ex.Message);
            return null;
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notificationRepository.ListAsync(new CancelableFollowUpsByOrderIdSpec(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            try
            {
                var updated = await _twilio.UpdateMessageAsync(
                    followUp.ProviderMessageSid!,
                    new TwilioUpdateMessageRequest { Status = "canceled" },
                    cancellationToken);

                if (updated != null)
                {
                    followUp.ApplyProviderState(updated.Status ?? "canceled", updated.ErrorCode, updated.Body);
                    await _notificationRepository.UpdateAsync(followUp, cancellationToken);
                }
                else
                {
                    _logger.LogWarning("Provider did not cancel follow-up {NotificationId} for order {OrderId}", followUp.Id, orderId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to cancel follow-up {NotificationId}: {Message}", followUp.Id, ex.Message);
            }
        }
    }

    private async Task RefreshStatusesAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _twilio.FetchMessageAsync(notification.ProviderMessageSid, cancellationToken);
                if (snapshot == null)
                {
                    continue;
                }

                notification.ApplyProviderState(snapshot.Status ?? notification.ProviderStatus, snapshot.ErrorCode, snapshot.Body);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to refresh notification {NotificationId}: {Message}", notification.Id, ex.Message);
            }
        }
    }

    private static ReconciledMessage ToReconciled(OrderNotification local, TwilioMessageSnapshot? provider)
        => new()
        {
            NotificationId = local.Id,
            ProviderSid = provider?.Sid ?? local.ProviderMessageSid,
            ProviderStatus = provider?.Status,
            EshopStatus = local.ProviderStatus,
            Kind = local.Kind,
            Body = local.ContentDisposed ? string.Empty : (provider?.Body ?? local.Body),
            CreatedAt = local.CreatedAt
        };
}
