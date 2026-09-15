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
/// POST /api/orders/{orderId}/cancel — operator action. Cancels the order before fulfilment,
/// releasing any held funds so no money ever moved.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, IPaymentService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
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
        var order = await service.CancelOrderAsync(orderId, ctx.RequestAborted);
        return Results.Ok(PaymentMapper.ToOrderDto(order));
    }
}
