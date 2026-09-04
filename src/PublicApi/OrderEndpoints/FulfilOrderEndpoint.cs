using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;
using BlazorShared.Authorization;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: marks an order fulfilled. This is when the money is actually taken —
/// the authorized payment is captured. A stale authorization is renewed first; one that
/// can no longer be renewed is reported so the operator can re-collect payment.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, int, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService paymentService) =>
            {
                return await HandleAsync(orderId, paymentService);
            })
            .Produces<FulfilOrderResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IPaymentService paymentService)
    {
        var result = await paymentService.CaptureOrderAsync(orderId);

        var response = new FulfilOrderResponse()
        {
            OrderId = result.OrderId,
            OrderStatus = result.OrderStatus,
            CaptureId = result.CaptureId,
            CaptureStatus = result.CaptureStatus,
            CapturedAmount = result.CapturedAmount,
            PayPalFee = result.PayPalFee,
            NetAmount = result.NetAmount,
            Currency = result.Currency
        };

        return Results.Ok(response);
    }
}