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

/// <summary>
/// POST /api/orders/{orderId}/fulfil — operator action. Marks the order fulfilled and captures
/// the money. A stale hold is renewed first; a hold that can no longer be renewed is reported.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, IPaymentService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IPaymentService service, HttpContext ctx) =>
                await HandleAsync(service, ctx))
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IPaymentService service, HttpContext ctx)
    {
        var orderId = PaymentMapper.GetRouteInt(ctx, "orderId");
        var order = await service.FulfilOrderAsync(orderId, ctx.RequestAborted);
        return Results.Ok(PaymentMapper.ToOrderDto(order));
    }
}
