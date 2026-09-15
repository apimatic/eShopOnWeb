using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersEndpoint : IEndpoint<IResult, ListMyOrdersRequest, IRepository<Order>>
{
    private readonly IOrderNotificationService _notifications;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListMyOrdersEndpoint(IOrderNotificationService notifications, IHttpContextAccessor httpContextAccessor)
    {
        _notifications = notifications;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IRepository<Order> orders) =>
            {
                return await HandleAsync(new ListMyOrdersRequest(), orders);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMyOrdersRequest request, IRepository<Order> orders)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var buyerId = httpContext?.User.GetBuyerId();
        if (httpContext is null || string.IsNullOrWhiteSpace(buyerId))
        {
            return Results.Unauthorized();
        }

        var myOrders = await orders.ListAsync(new CustomerOrdersSpecification(buyerId));
        var notifications = await _notifications.ListForBuyerAsync(buyerId, httpContext.RequestAborted);
        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        var response = new ListMyOrdersResponse(request.CorrelationId())
        {
            Orders = myOrders.Select(order => new MyOrderDto
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                OrderDate = order.OrderDate,
                Total = order.Total(),
                Notifications = byOrder.TryGetValue(order.Id, out var rows)
                    ? rows.Select(NotificationDto.From).ToList()
                    : new List<NotificationDto>()
            }).ToList()
        };

        return Results.Ok(response);
    }
}
