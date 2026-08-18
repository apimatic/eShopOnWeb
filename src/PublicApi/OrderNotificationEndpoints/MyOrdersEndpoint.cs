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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public int ItemCount { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId) { }
    public MyOrdersResponse() { }

    public List<OrderSummaryDto> Orders { get; set; } = new();
}

/// <summary>The signed-in shopper's own orders, each showing where its notifications got to.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, ClaimsPrincipal, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderNotificationService service) =>
            {
                return await HandleAsync(user, service);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderNotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IOrderNotificationService service)
    {
        var orders = await service.GetMyOrdersAsync(user.GetBuyerId());
        var response = new MyOrdersResponse
        {
            Orders = orders.Select(o => new OrderSummaryDto
            {
                OrderId = o.Order.Id,
                OrderDate = o.Order.OrderDate,
                Total = o.Order.Total(),
                ItemCount = o.Order.OrderItems.Count,
                Notifications = o.Notifications.Select(NotificationDto.From).ToList()
            }).ToList()
        };
        return Results.Ok(response);
    }
}
