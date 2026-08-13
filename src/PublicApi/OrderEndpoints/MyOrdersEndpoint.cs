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
using Microsoft.eShopWeb.PublicApi.Configuration;
using Microsoft.eShopWeb.PublicApi.Contracts;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrdersResponse
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

/// <summary>
/// The signed-in shopper's own orders, each showing where its notifications got to.
/// GET /api/my-orders
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, IOrderNotificationService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MyOrdersEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderNotificationService service) => await HandleAsync(service))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IOrderNotificationService service)
    {
        var buyerId = _httpContextAccessor.RequireUserId();
        var orders = await service.GetMyOrdersAsync(buyerId);

        var response = new MyOrdersResponse
        {
            Orders = orders.Select(o => new MyOrderDto
            {
                OrderId = o.Order.Id,
                Status = o.Order.Status.ToString(),
                OrderDate = o.Order.OrderDate,
                Total = o.Order.Total(),
                Notifications = o.Notifications.Select(NotificationDto.From).ToList()
            }).ToList()
        };
        return Results.Ok(response);
    }
}
