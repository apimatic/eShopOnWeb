using System;
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

public class ListMyOrdersEndpoint : IEndpoint<IResult, IRepository<Order>>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOrderNotificationService _notificationService;

    public ListMyOrdersEndpoint(
        IHttpContextAccessor httpContextAccessor,
        IOrderNotificationService notificationService)
    {
        _httpContextAccessor = httpContextAccessor;
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IRepository<Order> orderRepository) =>
            {
                return await HandleAsync(orderRepository);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IRepository<Order> orderRepository)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HTTP context is not available.");
        var buyerId = httpContext.GetRequiredBuyerId();
        var orders = await orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));

        var response = new ListMyOrdersResponse();
        foreach (var order in orders)
        {
            var notifications = await _notificationService.ListForOrderAsync(order.Id);
            response.Orders.Add(new MyOrderDto
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                OrderDate = order.OrderDate,
                Total = order.Total(),
                Notifications = notifications.Select(NotificationDtoMapper.From).ToList()
            });
        }

        return Results.Ok(response);
    }
}
