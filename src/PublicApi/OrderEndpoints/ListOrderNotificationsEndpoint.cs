using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Shows what was sent for an order and what became of each message. Available
/// to the shopper who owns the order and to operators.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListOrderNotificationsEndpoint : EndpointBaseAsync
    .WithRequest<int>
    .WithActionResult<ListOrderNotificationsResponse>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IOrderNotificationService _notificationService;

    public ListOrderNotificationsEndpoint(IRepository<Order> orderRepository,
        IRepository<OrderNotification> notificationRepository,
        IOrderNotificationService notificationService)
    {
        _orderRepository = orderRepository;
        _notificationRepository = notificationRepository;
        _notificationService = notificationService;
    }

    [HttpGet("api/orders/{orderId}/notifications")]
    [SwaggerOperation(
        Summary = "Lists an order's notifications",
        Description = "Lists what was sent for the order and the outcome of each message",
        OperationId = "orders.listNotifications",
        Tags = new[] { "OrderEndpoints" })
    ]
    public override async Task<ActionResult<ListOrderNotificationsResponse>> HandleAsync(
        [FromRoute(Name = "orderId")] int request, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(request, cancellationToken);
        if (order == null)
        {
            return NotFound();
        }

        var isAdmin = User.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
        if (!isAdmin && order.BuyerId != User.Identity!.Name)
        {
            return NotFound();
        }

        var notifications = await _notificationRepository.ListAsync(
            new NotificationsByOrderSpecification(order.Id), cancellationToken);
        await _notificationService.RefreshStatusesAsync(notifications, cancellationToken);

        return new ListOrderNotificationsResponse
        {
            OrderId = order.Id,
            Notifications = notifications.Select(NotificationDto.FromEntity).ToList()
        };
    }
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}
