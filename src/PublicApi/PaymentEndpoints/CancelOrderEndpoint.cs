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
/// Operator action: releases the authorization before fulfilment.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IFinixPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
                [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (int orderId, IFinixPaymentService paymentService) =>
                {
                    return await HandleAsync(new CancelOrderRequest { OrderId = orderId }, paymentService);
                })
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IFinixPaymentService paymentService)
    {
        var order = await paymentService.CancelOrderAsync(request);
        return Results.Ok(order);
    }
}
