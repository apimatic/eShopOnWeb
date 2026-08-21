using System;
using System.Collections.Generic;
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

public class MyOrdersResponse : BaseResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class GetMyOrdersEndpoint : IEndpoint<IResult, HttpContext, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, IShopperOrderService shopperOrderService) =>
            {
                return await HandleAsync(httpContext, shopperOrderService);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext httpContext, IShopperOrderService shopperOrderService)
    {
        var buyerId = ShopperIdentity.TryGetBuyerId(httpContext);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var summaries = await shopperOrderService.ListMyOrdersAsync(buyerId);
        var response = new MyOrdersResponse
        {
            Orders = summaries.Select(s => new MyOrderDto
            {
                OrderId = s.Order.Id,
                Status = s.Order.Status.ToString(),
                OrderDate = s.Order.OrderDate,
                Total = s.Order.Total(),
                Notifications = s.Notifications.Select(NotificationDto.From).ToList()
            }).ToList()
        };

        return Results.Ok(response);
    }
}
