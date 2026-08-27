using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrderDto : OrderDto
{
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}

/// <summary>
/// Lists the caller's orders, each with where its notifications got to.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListMyOrdersEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<List<MyOrderDto>>
{
    private readonly IRepository<Order> _orders;
    private readonly IRepository<OrderNotification> _notifications;

    public ListMyOrdersEndpoint(IRepository<Order> orders, IRepository<OrderNotification> notifications)
    {
        _orders = orders;
        _notifications = notifications;
    }

    [HttpGet("api/my-orders")]
    [SwaggerOperation(Summary = "Lists the caller's orders with their notification outcomes", Tags = new[] { "OrderEndpoints" })]
    public override async Task<ActionResult<List<MyOrderDto>>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var buyerId = User.GetBuyerId();
        if (buyerId is null) return Unauthorized();

        var orders = await _orders.ListAsync(new OrdersByBuyerSpecification(buyerId), cancellationToken);
        var result = new List<MyOrderDto>();
        foreach (var order in orders)
        {
            var notifications = await _notifications.ListAsync(new NotificationsByOrderSpecification(order.Id), cancellationToken);
            result.Add(new MyOrderDto
            {
                OrderId = order.Id,
                OrderDate = order.OrderDate,
                Status = order.Status.ToString(),
                Total = order.Total(),
                Notifications = notifications.Select(OrderNotificationDto.FromEntity).ToList()
            });
        }
        return result;
    }
}
