using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    private readonly INotificationGateway _gateway;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        INotificationGateway gateway,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken ct = default)
    {
        await SendToShopperAsync(order, NotificationKind.OrderPlaced,
            $"eShop: thank you! Your order #{order.Id} has been placed.", ct);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken ct = default)
    {
        await SendToShopperAsync(order, NotificationKind.OrderDispatched,
            $"eShop: good news — your order #{order.Id} is on its way!", ct);

        var contactNumber = await GetShopperNumberAsync(order.BuyerId, ct);
        if (contactNumber is null)
        {
            return;
        }

        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var notification = new OrderNotification(order.Id, order.BuyerId, NotificationKind.DeliveryFollowUp,
            contactNumber.PhoneNumber,
            $"eShop: how did the delivery of your order #{order.Id} go? We'd love to hear.",
            scheduledFor: sendAt);
        try
        {
            var message = await _gateway.ScheduleMessageAsync(contactNumber.PhoneNumber, notification.Body!, sendAt, ct);
            notification.MarkAccepted(message.Sid, message.Status ?? "scheduled");
        }
        catch (Exception ex) when (ex is NotificationProviderException or OperationCanceledException)
        {
            _logger.LogWarning($"Delivery follow-up for order {order.Id} could not be scheduled: {ex.Message}");
            notification.MarkFailed(NotificationStatuses.Failed,
                (ex as NotificationProviderException)?.ProviderErrorCode, null);
        }
        await _notificationRepository.AddAsync(notification, ct);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken ct = default)
    {
        await CancelPendingFollowUpsAsync(order, ct);

        await SendToShopperAsync(order, NotificationKind.OrderCancelled,
            $"eShop: your order #{order.Id} has been cancelled. Please contact support if this is unexpected.", ct);
    }

    private async Task CancelPendingFollowUpsAsync(Order order, CancellationToken ct)
    {
        var followUps = await _notificationRepository.ListAsync(new PendingFollowUpsSpecification(order.Id), ct);
        foreach (var followUp in followUps)
        {
            try
            {
                var current = await GetMessageWithRetryAsync(followUp.MessageSid!, ct);
                if (string.Equals(current.Status, "scheduled", StringComparison.OrdinalIgnoreCase))
                {
                    await CancelWithRetryAsync(followUp.MessageSid!, ct);
                    followUp.UpdateFromProvider("canceled", null, null);
                }
                else
                {
                    followUp.UpdateFromProvider(current.Status ?? followUp.Status,
                        current.ErrorCode, current.ErrorMessage);
                }
                await _notificationRepository.UpdateAsync(followUp, ct);
            }
            catch (Exception ex) when (ex is NotificationProviderException or OperationCanceledException)
            {
                _logger.LogWarning($"Could not cancel scheduled follow-up {followUp.MessageSid} for order {order.Id}: {ex.Message}");
            }
        }
    }

    // A just-scheduled message can 404 on fetch/update for a short window while the
    // provider propagates it; retry briefly before giving up (never fails the cancel).
    private async Task<ProviderMessage> GetMessageWithRetryAsync(string messageSid, CancellationToken ct)
    {
        return await RetryOnNotFoundAsync(() => _gateway.GetMessageAsync(messageSid, ct), ct);
    }

    private async Task CancelWithRetryAsync(string messageSid, CancellationToken ct)
    {
        await RetryOnNotFoundAsync(async () =>
        {
            await _gateway.CancelScheduledMessageAsync(messageSid, ct);
            return true;
        }, ct);
    }

    private async Task<T> RetryOnNotFoundAsync<T>(Func<Task<T>> call, CancellationToken ct)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await call();
            }
            catch (NotificationProviderException ex) when (ex.ProviderStatusCode == System.Net.HttpStatusCode.NotFound
                && attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    private async Task SendToShopperAsync(Order order, NotificationKind kind, string body, CancellationToken ct)
    {
        var contactNumber = await GetShopperNumberAsync(order.BuyerId, ct);
        if (contactNumber is null)
        {
            _logger.LogInformation($"Order {order.Id}: shopper has no contact number on file; no {kind} message sent.");
            return;
        }

        var notification = new OrderNotification(order.Id, order.BuyerId, kind, contactNumber.PhoneNumber, body);
        try
        {
            var message = await _gateway.SendMessageAsync(contactNumber.PhoneNumber, body, ct);
            notification.MarkAccepted(message.Sid, message.Status ?? "accepted");
            notification.UpdateFromProvider(message.Status ?? "accepted", message.ErrorCode, message.ErrorMessage);
        }
        catch (Exception ex) when (ex is NotificationProviderException or OperationCanceledException)
        {
            _logger.LogWarning($"Order {order.Id}: {kind} message could not be sent: {ex.Message}");
            notification.MarkFailed(NotificationStatuses.Failed,
                (ex as NotificationProviderException)?.ProviderErrorCode, null);
        }
        await _notificationRepository.AddAsync(notification, ct);
    }

    private async Task<ContactNumber?> GetShopperNumberAsync(string buyerId, CancellationToken ct)
    {
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(buyerId), ct);
        return numbers.FirstOrDefault();
    }
}
