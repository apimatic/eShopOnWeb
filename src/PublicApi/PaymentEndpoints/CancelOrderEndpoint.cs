using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public record CancelOrderCommand(int OrderId);

/// <summary>
/// Operator action: cancels an order before fulfilment, releasing any held funds so no money moved.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderCommand, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService paymentService) =>
                await HandleAsync(new CancelOrderCommand(orderId), paymentService))
            .Produces<PaymentStateResponse>()
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(CancelOrderCommand request, IPaymentService paymentService)
    {
        var payment = await paymentService.CancelAsync(request.OrderId);
        return Results.Ok(PaymentStateResponse.FromPayment(payment));
    }
}
