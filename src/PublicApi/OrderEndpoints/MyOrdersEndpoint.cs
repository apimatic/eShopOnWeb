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

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId) { }
    public MyOrdersResponse() { }
    public List<OrderDto> Orders { get; set; } = new();
}

/// <summary>
/// GET /api/my-orders — the signed-in shopper's orders with their payment state. Only ever the
/// caller's own orders.
/// </summary>
public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderPaymentService service) =>
                await HandleAsync(user, service))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    private static async Task<IResult> HandleAsync(ClaimsPrincipal user, IOrderPaymentService service)
    {
        var buyerId = user.GetBuyerId();
        if (buyerId is null) return Results.Unauthorized();

        var orders = await service.GetOrdersForBuyerAsync(buyerId);
        return Results.Ok(new MyOrdersResponse { Orders = orders.Select(OrderDto.From).ToList() });
    }
}
