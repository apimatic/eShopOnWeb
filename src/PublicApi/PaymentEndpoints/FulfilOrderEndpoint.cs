using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Operator action: captures the held funds and marks the order fulfilled.</summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, int, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
                [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (int orderId, IOrderPaymentService paymentService) =>
                {
                    return await HandleAsync(orderId, paymentService);
                })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IOrderPaymentService paymentService)
    {
        var payment = await paymentService.FulfilAsync(orderId);
        var response = new FulfilOrderResponse
        {
            OrderId = orderId,
            OrderStatus = payment.Status is "Captured" or "PartiallyRefunded" or "Refunded"
                ? "Fulfilled"
                : payment.Status,
            Payment = payment
        };

        return Results.Ok(response);
    }
}
