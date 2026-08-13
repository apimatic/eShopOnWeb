using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Configuration;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>The caller's own orders, each showing where its notifications got to.</summary>
public class ListMyOrdersEndpoint : IEndpoint<IResult, ClaimsPrincipal, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderNotificationService service, CancellationToken ct) =>
            {
                return await HandleAsync(user, service, ct);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(ClaimsPrincipal user, IOrderNotificationService service) =>
        HandleAsync(user, service, default);

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IOrderNotificationService service, CancellationToken ct)
    {
        var callerId = user.GetCallerId();
        if (string.IsNullOrEmpty(callerId))
        {
            return Results.Unauthorized();
        }

        var orders = await service.GetOrdersForBuyerAsync(callerId, ct);

        var response = new ListMyOrdersResponse
        {
            Orders = orders.Select(o => new MyOrderDto
            {
                OrderId = o.Order.Id,
                OrderDate = o.Order.OrderDate,
                Status = o.Order.Status.ToString(),
                Total = o.Order.Total(),
                Notifications = o.Notifications.Select(NotificationDto.FromEntity).ToList()
            }).ToList()
        };

        return Results.Ok(response);
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
