using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action. Marks the order fulfilled, and that is when the held money is actually taken.
/// A hold that has gone stale is renewed first rather than failing the fulfilment outright.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, int, IPaymentService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService paymentService, HttpContext context) =>
            {
                return await HandleAsync(orderId, paymentService, context);
            })
            .Produces<PaymentResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IPaymentService paymentService, HttpContext context)
    {
        var payment = await paymentService.FulfilAsync(orderId, context.RequestAborted);
        return Results.Ok(new PaymentResponse { Payment = payment });
    }
}
