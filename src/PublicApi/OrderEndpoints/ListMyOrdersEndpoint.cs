using System;
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
/// Lists the signed-in shopper's orders, each showing where its notifications
/// got to.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListMyOrdersEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListMyOrdersResponse>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IOrderNotificationService _notificationService;

    public ListMyOrdersEndpoint(IRepository<Order> orderRepository,
        IRepository<OrderNotification> notificationRepository,
        IOrderNotificationService notificationService)
    {
        _orderRepository = orderRepository;
        _notificationRepository = notificationRepository;
        _notificationService = notificationService;
    }

    [HttpGet("api/my-orders")]
    [SwaggerOperation(
        Summary = "Lists the caller's orders",
        Description = "Lists the caller's orders with the state of their notifications",
        OperationId = "orders.listMine",
        Tags = new[] { "OrderEndpoints" })
    ]
    public override async Task<ActionResult<ListMyOrdersResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(
            new CustomerOrdersWithItemsSpecification(User.Identity!.Name!), cancellationToken);

        var response = new ListMyOrdersResponse();
        foreach (var order in orders.OrderByDescending(o => o.OrderDate))
        {
            var notifications = await _notificationRepository.ListAsync(
                new NotificationsByOrderSpecification(order.Id), cancellationToken);
            await _notificationService.RefreshStatusesAsync(notifications, cancellationToken);

            response.Orders.Add(new MyOrderDto
            {
                OrderId = order.Id,
                OrderDate = order.OrderDate,
                Status = order.Status.ToString(),
                Total = order.Total(),
                Notifications = notifications.Select(NotificationDto.FromEntity).ToList()
            });
        }
        return response;
    }
}

public class ListMyOrdersResponse : BaseResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}
