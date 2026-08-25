using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public record FulfilOrderResponse(int OrderId, string? CaptureId, string? CaptureStatus, string? CapturedAmount);

public class FulfilOrderEndpoint : IEndpoint<IResult, int, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IPaymentService svc) => await HandleAsync(orderId, svc))
            .Produces<FulfilOrderResponse>()
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(int orderId, IPaymentService svc)
    {
        var payment = await svc.FulfilOrderAsync(orderId);
        return Results.Ok(new FulfilOrderResponse(orderId, payment.CaptureId, payment.CaptureStatus, payment.CapturedAmountValue));
    }
}
