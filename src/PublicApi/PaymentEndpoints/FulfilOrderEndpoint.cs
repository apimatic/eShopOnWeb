using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Route id for an order-level operator command.</summary>
public record OrderIdCommand(int OrderId);

/// <summary>
/// Operator action (administrator). Fulfils the order and captures the held funds. Renews a stale
/// authorization rather than failing. Idempotent.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, OrderIdCommand, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService paymentService) =>
                await HandleAsync(new OrderIdCommand(orderId), paymentService))
            .Produces<PaymentResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderIdCommand command, IPaymentService paymentService)
    {
        var payment = await paymentService.FulfilOrderAsync(command.OrderId);
        return Results.Ok(PaymentMapping.ToPaymentResponse(payment));
    }
}
