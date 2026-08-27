using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    // The follow-up is queued with the provider this long after dispatch.
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ITextMessagingService _messagingService;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        ITextMessagingService messagingService,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _messagingService = messagingService;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken ct = default)
    {
        var body = string.Create(CultureInfo.InvariantCulture,
            $"eShopOnWeb: thank you! Your order #{order.Id} ({order.Total():0.00} total) has been placed. We'll text you when it ships.");
        await SendToShopperAsync(order, NotificationKind.OrderPlaced, body, ct);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken ct = default)
    {
        var body = $"eShopOnWeb: good news — your order #{order.Id} is on its way!";
        await SendToShopperAsync(order, NotificationKind.OrderDispatched, body, ct);

        var followUpBody = $"eShopOnWeb: how did the delivery of your order #{order.Id} go? We'd love to know.";
        await ScheduleFollowUpAsync(order, followUpBody, DateTimeOffset.UtcNow.Add(FollowUpDelay), ct);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken ct = default)
    {
        await CancelPendingFollowUpsAsync(order, ct);

        var body = $"eShopOnWeb: your order #{order.Id} has been cancelled. Please contact support if this is unexpected.";
        await SendToShopperAsync(order, NotificationKind.OrderCancelled, body, ct);
    }

    private async Task SendToShopperAsync(Order order, NotificationKind kind, string body, CancellationToken ct)
    {
        var contactNumber = await GetShopperContactNumberAsync(order.BuyerId, ct);
        if (contactNumber is null)
        {
            _logger.LogInformation($"No contact number on file for order {order.Id}; skipping {kind} notification.");
            return;
        }

        var notification = new OrderNotification(order.Id, order.BuyerId, contactNumber.Id, kind, body);
        notification = await _notificationRepository.AddAsync(notification, ct);

        try
        {
            var result = await _messagingService.SendMessageAsync(contactNumber.PhoneNumber, body, ct);
            notification.MarkAccepted(result.Sid, result.Status);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning($"Failed to send {kind} notification for order {order.Id}: {ex.Message}");
            notification.MarkSendFailed(ex.Message);
        }

        await _notificationRepository.UpdateAsync(notification, ct);
    }

    private async Task ScheduleFollowUpAsync(Order order, string body, DateTimeOffset sendAt, CancellationToken ct)
    {
        var contactNumber = await GetShopperContactNumberAsync(order.BuyerId, ct);
        if (contactNumber is null)
        {
            return;
        }

        var notification = new OrderNotification(order.Id, order.BuyerId, contactNumber.Id, NotificationKind.DeliveryFollowUp, body);
        notification = await _notificationRepository.AddAsync(notification, ct);

        try
        {
            var result = await _messagingService.ScheduleMessageAsync(contactNumber.PhoneNumber, body, sendAt, ct);
            notification.MarkAccepted(result.Sid, result.Status, sendAt);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning($"Failed to schedule delivery follow-up for order {order.Id}: {ex.Message}");
            notification.MarkSendFailed(ex.Message);
        }

        await _notificationRepository.UpdateAsync(notification, ct);
    }

    private async Task CancelPendingFollowUpsAsync(Order order, CancellationToken ct)
    {
        var pending = await _notificationRepository.ListAsync(new PendingFollowUpsForOrderSpecification(order.Id), ct);
        foreach (var followUp in pending)
        {
            try
            {
                var result = await _messagingService.CancelScheduledMessageAsync(followUp.MessageSid!, ct);
                followUp.UpdateDeliveryState(result.Status, result.ErrorCode, result.ErrorMessage);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The provider refused the cancel (e.g. it already went out) or is unreachable;
                // settle the record from the provider's own current state where possible.
                _logger.LogWarning($"Failed to cancel scheduled follow-up for order {order.Id}: {ex.Message}");
                try
                {
                    var current = await _messagingService.GetMessageAsync(followUp.MessageSid!, ct);
                    followUp.UpdateDeliveryState(current.Status, current.ErrorCode, current.ErrorMessage);
                }
                catch (Exception readEx) when (readEx is not OperationCanceledException)
                {
                    _logger.LogWarning($"Could not read back follow-up state for order {order.Id}: {readEx.Message}");
                }
            }

            await _notificationRepository.UpdateAsync(followUp, ct);
        }
    }

    private async Task<ContactNumber?> GetShopperContactNumberAsync(string buyerId, CancellationToken ct)
    {
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(buyerId), ct);
        return numbers.FirstOrDefault();
    }
}
