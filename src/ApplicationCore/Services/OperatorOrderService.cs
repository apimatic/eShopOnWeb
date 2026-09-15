using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OperatorOrderService : IOperatorOrderService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orders;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IOrderNotificationSender _notificationSender;
    private readonly ISmsMessageGateway _smsGateway;
    private readonly IAppLogger<OperatorOrderService> _logger;

    public OperatorOrderService(
        IRepository<Order> orders,
        IRepository<OrderNotification> notifications,
        IOrderNotificationSender notificationSender,
        ISmsMessageGateway smsGateway,
        IAppLogger<OperatorOrderService> logger)
    {
        _orders = orders;
        _notifications = notifications;
        _notificationSender = notificationSender;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order == null)
        {
            throw new OrderNotFoundException();
        }

        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);

        await _notificationSender.TryNotifyAsync(
            order,
            NotificationKind.OrderDispatched,
            $"eShopOnWeb: Order #{order.Id} is on its way.",
            cancellationToken: cancellationToken);

        var sendAt = DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay);
        await _notificationSender.TryNotifyAsync(
            order,
            NotificationKind.DeliveryFollowUp,
            $"eShopOnWeb: How did the delivery of order #{order.Id} go?",
            sendAt,
            cancellationToken: cancellationToken);

        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order == null)
        {
            throw new OrderNotFoundException();
        }

        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);

        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        await _notificationSender.TryNotifyAsync(
            order,
            NotificationKind.OrderCancelled,
            $"eShopOnWeb: Order #{order.Id} has been cancelled.",
            cancellationToken: cancellationToken);

        return order;
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notifications.ListAsync(new PendingFollowUpNotificationSpecification(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            await _notificationSender.SyncFromProviderAsync(followUp, cancellationToken);
            if (!followUp.CanBeCancelledAtProvider() || string.IsNullOrEmpty(followUp.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var result = await _smsGateway.CancelAsync(followUp.ProviderMessageSid, cancellationToken);
                followUp.ApplyProviderState(result.Status, result.ErrorCode, result.ErrorMessage);
                await _notifications.UpdateAsync(followUp, cancellationToken);
                await _notificationSender.SyncFromProviderAsync(followUp, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to cancel scheduled follow-up {NotificationId} for order {OrderId}: {Message}", followUp.Id, orderId, ex.Message);
            }
        }
    }
}
