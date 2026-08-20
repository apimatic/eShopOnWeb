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
    private readonly IOrderNotificationService _notificationService;

    public ListMyOrdersEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, IRepository<Order> orderRepository) =>
            {
                return await HandleAsync(new ListMyOrdersRequest { BuyerId = httpContext.GetRequiredBuyerId() }, orderRepository);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMyOrdersRequest request, IRepository<Order> orderRepository)
    {
        var orders = await orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(request.BuyerId));
        var response = new ListMyOrdersResponse();

        foreach (var order in orders)
        {
            var notifications = await _notificationService.ListForOrderAsync(order.Id);
            response.Orders.Add(new ShopperOrderDto
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                OrderDate = order.OrderDate,
                Total = order.Total(),
                Notifications = notifications.Select(NotificationDtoMapper.ToDto).ToList()
            });
        }

        return Results.Ok(response);
    }
}

public class ListMyOrdersRequest : BaseRequest
{
    internal string BuyerId { get; set; } = string.Empty;
}

public class ListMyOrdersResponse : BaseResponse
{
    public List<ShopperOrderDto> Orders { get; set; } = new();
}

public class ShopperOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public System.DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}
