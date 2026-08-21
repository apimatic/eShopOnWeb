using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersEndpoint : IEndpoint<IResult, HttpContext, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, IShopperOrderService orders) =>
            {
                return await HandleAsync(http, orders);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http, IShopperOrderService orders)
    {
        var buyerId = BuyerIdentity.GetBuyerId(http);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var summaries = await orders.ListMyOrdersAsync(buyerId);
        return Results.Ok(new ListMyOrdersResponse
        {
            Orders = summaries.Select(s => new MyOrderDto
            {
                OrderId = s.Order.Id,
                Status = s.Order.Status.ToString(),
                OrderDate = s.Order.OrderDate,
                Total = s.Order.Total(),
                Notifications = s.Notifications.Select(NotificationMapper.ToDto).ToList()
            }).ToList()
        });
    }
}

public class ListMyOrdersResponse
{
    public System.Collections.Generic.List<MyOrderDto> Orders { get; set; } = new();
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public System.DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public System.Collections.Generic.List<NotificationDto> Notifications { get; set; } = new();
}
