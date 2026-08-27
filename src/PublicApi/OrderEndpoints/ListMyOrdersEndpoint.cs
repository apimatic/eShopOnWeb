using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists the signed-in shopper's orders, each with where its notifications got to.
/// </summary>
public class ListMyOrdersEndpoint : IEndpoint<IResult, ListMyOrdersRequest, ClaimsPrincipal>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;

    public ListMyOrdersEndpoint(IRepository<Order> orderRepository,
        IRepository<OrderNotification> notificationRepository)
    {
        _orderRepository = orderRepository;
        _notificationRepository = notificationRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user) =>
            {
                return await HandleAsync(new ListMyOrdersRequest(), user);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMyOrdersRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));
        var orderIds = orders.Select(o => o.Id).ToList();
        var notifications = orderIds.Count == 0
            ? new List<OrderNotification>()
            : await _notificationRepository.ListAsync(new NotificationsByOrderIdsSpecification(orderIds));

        var response = new ListMyOrdersResponse(request.CorrelationId());
        response.Orders = orders.Select(o => new MyOrderDto
        {
            OrderId = o.Id,
            OrderDate = o.OrderDate,
            Status = o.Status.ToString(),
            Total = o.Total(),
            ItemCount = o.OrderItems.Sum(i => i.Units),
            Notifications = notifications
                .Where(n => n.OrderId == o.Id)
                .Select(n => OrderNotificationDto.FromEntity(n))
                .ToList()
        }).ToList();

        return Results.Ok(response);
    }
}

public class ListMyOrdersRequest : BaseRequest
{
}

public class ListMyOrdersResponse : BaseResponse
{
    public ListMyOrdersResponse(Guid correlationId) : base(correlationId) {}
    public ListMyOrdersResponse() {}

    public List<MyOrderDto> Orders { get; set; } = new();
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public int ItemCount { get; set; }
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}
