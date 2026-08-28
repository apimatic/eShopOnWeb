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
/// Operator action. Cancels an order before fulfilment, releasing the shopper's held funds so no
/// money ever moved. An order already fulfilled must be refunded instead.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int, IPaymentService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
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
        var payment = await paymentService.CancelAsync(orderId, context.RequestAborted);
        return Results.Ok(new PaymentResponse { Payment = payment });
    }
}
