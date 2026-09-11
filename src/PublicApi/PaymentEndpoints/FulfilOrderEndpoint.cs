using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public record FulfilOrderCommand(int OrderId);

/// <summary>
/// Operator action: fulfils the order, capturing (taking) the held funds. A stale authorization is
/// renewed first; one that can no longer be renewed reports an operator-actionable message.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderCommand, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService paymentService) =>
                await HandleAsync(new FulfilOrderCommand(orderId), paymentService))
            .Produces<PaymentStateResponse>()
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(FulfilOrderCommand request, IPaymentService paymentService)
    {
        var payment = await paymentService.FulfilAsync(request.OrderId);
        return Results.Ok(PaymentStateResponse.FromPayment(payment));
    }
}
