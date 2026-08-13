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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>The caller's own orders, each showing where its notifications got to.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, INotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, INotificationService service) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var orders = await service.GetOrdersForBuyerAsync(buyerId);
                var response = new MyOrdersResponse
                {
                    Orders = orders.Select(o => new MyOrderDto(
                        o.Order.Id,
                        o.Order.OrderDate,
                        o.Order.Total(),
                        o.Notifications.Select(OrderNotificationDto.FromEntity).ToList())).ToList()
                };
                return Results.Ok(response);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(INotificationService service) =>
        Task.FromResult<IResult>(Results.Empty);
}

public record MyOrderDto(int OrderId, DateTimeOffset OrderDate, decimal Total, List<OrderNotificationDto> Notifications);

public class MyOrdersResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}
