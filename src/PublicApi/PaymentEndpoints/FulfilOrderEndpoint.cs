using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Operator action: captures the authorization and marks the order fulfilled.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IFinixPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
                [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (int orderId, IFinixPaymentService paymentService) =>
                {
                    return await HandleAsync(new FulfilOrderRequest { OrderId = orderId }, paymentService);
                })
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, IFinixPaymentService paymentService)
    {
        var order = await paymentService.FulfilOrderAsync(request);
        return Results.Ok(order);
    }
}
