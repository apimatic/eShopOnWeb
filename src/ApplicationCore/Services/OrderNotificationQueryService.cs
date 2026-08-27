using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationQueryService : IOrderNotificationQueryService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ITextMessagingService _messagingService;
    private readonly IAppLogger<OrderNotificationQueryService> _logger;

    public OrderNotificationQueryService(
        IRepository<Order> orderRepository,
        IRepository<OrderNotification> notificationRepository,
        ITextMessagingService messagingService,
        IAppLogger<OrderNotificationQueryService> logger)
    {
        _orderRepository = orderRepository;
        _notificationRepository = notificationRepository;
        _messagingService = messagingService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<OrderNotification>?> GetForOrderAsync(int orderId, string buyerId, CancellationToken ct = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null || order.BuyerId != buyerId)
        {
            return null;
        }

        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), ct);

        foreach (var notification in notifications.Where(n => n.MessageSid is not null))
        {
            try
            {
                var current = await _messagingService.GetMessageAsync(notification.MessageSid!, ct);
                notification.UpdateDeliveryState(current.Status, current.ErrorCode, current.ErrorMessage);
                await _notificationRepository.UpdateAsync(notification, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Provider unreachable or message unreadable — report the last known state.
                _logger.LogWarning($"Could not refresh notification {notification.Id} state: {ex.Message}");
            }
        }

        return notifications;
    }
}
