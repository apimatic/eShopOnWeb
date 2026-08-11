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
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrdersResponse
{
    public List<OrderDto> Orders { get; set; } = new();
}

/// <summary>GET /api/my-orders — the caller's orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, IOrderPaymentService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderPaymentService service, ClaimsPrincipal user) =>
                await HandleAsync(service, user))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IOrderPaymentService service, ClaimsPrincipal user)
    {
        var buyerId = user.GetBuyerId();
        var orders = await service.GetMyOrdersAsync(buyerId);
        var response = new MyOrdersResponse
        {
            Orders = orders.OrderByDescending(o => o.OrderDate).Select(o => o.ToDto()).ToList()
        };
        return Results.Ok(response);
    }
}
