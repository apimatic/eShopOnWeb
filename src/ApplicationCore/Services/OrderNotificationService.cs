using System;
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

    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ISmsProvider _smsProvider;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        ISmsProvider smsProvider,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _smsProvider = smsProvider;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken ct = default)
    {
        var body = $"eShopOnWeb: your order #{order.Id} has been placed. Total: {order.Total():C}. Thank you for shopping with us!";
        await SendToShopperNumbersAsync(order, OrderNotificationKind.OrderPlaced, body, null, ct);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken ct = default)
    {
        var body = $"eShopOnWeb: good news — your order #{order.Id} is on its way!";
        await SendToShopperNumbersAsync(order, OrderNotificationKind.OrderDispatched, body, null, ct);

        var followUpBody = $"eShopOnWeb: how did the delivery of your order #{order.Id} go? We'd love to hear from you.";
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        await SendToShopperNumbersAsync(order, OrderNotificationKind.DeliveryFollowUp, followUpBody, sendAt, ct);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken ct = default)
    {
        var body = $"eShopOnWeb: your order #{order.Id} has been cancelled. If this is unexpected, please contact support.";
        await SendToShopperNumbersAsync(order, OrderNotificationKind.OrderCancelled, body, null, ct);

        await CancelPendingFollowUpsAsync(order, ct);
    }

    public async Task<ResendNotificationResult?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct = default)
    {
        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpecification(idempotencyKey), ct);
        if (existing is not null)
        {
            return new ResendNotificationResult(existing, true);
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, ct);
        if (original is null)
        {
            return null;
        }

        var body = original.Body ?? string.Empty;
        var notification = new OrderNotification(original.OrderId, original.BuyerId, original.ToNumber,
            body, OrderNotificationKind.Resend, null, idempotencyKey);

        await SendAndRecordAsync(notification, body, null, ct);
        return new ResendNotificationResult(notification, false);
    }

    public async Task RefreshStatusAsync(OrderNotification notification, CancellationToken ct = default)
    {
        if (notification.MessageSid is null || notification.ContentRedacted)
        {
            return;
        }

        try
        {
            var state = await _smsProvider.GetMessageAsync(notification.MessageSid, ct);
            if (state.Status is not null && state.Status != notification.Status)
            {
                notification.UpdateProviderState(state.Status, state.ErrorCode, state.ErrorMessage);
                await _notificationRepository.UpdateAsync(notification, ct);
            }
        }
        catch (SmsProviderException ex)
        {
            _logger.LogWarning("Could not refresh notification {NotificationId} (message {MessageSid}): {Message}",
                notification.Id, notification.MessageSid, ex.Message);
        }
    }

    private async Task SendToShopperNumbersAsync(Order order, OrderNotificationKind kind, string body,
        DateTimeOffset? scheduleFor, CancellationToken ct)
    {
        var numbers = await _contactNumberRepository.ListAsync(
            new ContactNumbersByBuyerSpecification(order.BuyerId), ct);
        if (numbers.Count == 0)
        {
            return; // a shopper with no number on file is simply not messaged
        }

        foreach (var number in numbers)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, number.PhoneNumber, body, kind, scheduleFor);
            await SendAndRecordAsync(notification, body, scheduleFor, ct);
        }
    }

    private async Task SendAndRecordAsync(OrderNotification notification, string body,
        DateTimeOffset? scheduleFor, CancellationToken ct)
    {
        try
        {
            var result = scheduleFor is null
                ? await _smsProvider.SendAsync(notification.ToNumber, body, ct)
                : await _smsProvider.ScheduleAsync(notification.ToNumber, body, scheduleFor.Value, ct);

            if (result.Success && result.MessageSid is not null)
            {
                notification.MarkAccepted(result.MessageSid, result.ProviderStatus ?? "accepted");
            }
            else
            {
                notification.MarkSendFailed(result.ErrorMessage, result.ProviderErrorCode);
            }
        }
        catch (SmsProviderException ex)
        {
            // A message that cannot be sent must never fail the underlying operation.
            notification.MarkSendFailed(ex.Message, (int?)ex.StatusCode);
            _logger.LogWarning("SMS send for order {OrderId} (kind {Kind}) failed: {Message}",
                notification.OrderId, notification.Kind, ex.Message);
        }

        await _notificationRepository.AddAsync(notification, ct);
    }

    private async Task CancelPendingFollowUpsAsync(Order order, CancellationToken ct)
    {
        var notifications = await _notificationRepository.ListAsync(
            new NotificationsByOrderSpecification(order.Id), ct);

        var pendingFollowUps = notifications
            .Where(n => n.Kind == OrderNotificationKind.DeliveryFollowUp
                        && n.MessageSid is not null
                        && n.Status == "scheduled")
            .ToList();

        foreach (var followUp in pendingFollowUps)
        {
            try
            {
                var result = await _smsProvider.CancelScheduledAsync(followUp.MessageSid!, ct);
                if (result.Success)
                {
                    followUp.UpdateProviderState(result.ProviderStatus ?? "canceled", null, null);
                }
                else
                {
                    followUp.UpdateProviderState(followUp.Status, result.ProviderErrorCode, result.ErrorMessage);
                }
                await _notificationRepository.UpdateAsync(followUp, ct);
            }
            catch (SmsProviderException ex)
            {
                // Never fail the cancel operation; surface the provider's answer on the record.
                followUp.UpdateProviderState(followUp.Status, (int?)ex.StatusCode, ex.Message);
                await _notificationRepository.UpdateAsync(followUp, ct);
                _logger.LogWarning("Could not cancel scheduled follow-up {MessageSid} for order {OrderId}: {Message}",
                    followUp.MessageSid, order.Id, ex.Message);
            }
        }
    }
}
