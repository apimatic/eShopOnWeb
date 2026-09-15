using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>GET /api/my-orders — the caller's own orders, each with its payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, IPaymentService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IPaymentService service, HttpContext ctx) =>
                await HandleAsync(service, ctx))
            .Produces<List<OrderDto>>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IPaymentService service, HttpContext ctx)
    {
        var buyerId = PaymentMapper.GetBuyerId(ctx.User);
        var orders = await service.GetMyOrdersAsync(buyerId, ctx.RequestAborted);
        return Results.Ok(orders.Select(PaymentMapper.ToOrderDto).ToList());
    }
}
