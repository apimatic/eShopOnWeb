using System.Threading;
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
/// Operator action: captures the held authorization, taking the money. Transparently renews a
/// stale authorization first.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService paymentService) =>
            {
                return await HandleAsync(new FulfilOrderRequest(orderId), paymentService);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, IPaymentService paymentService)
    {
        var response = new FulfilOrderResponse(request.CorrelationId());

        var payment = await paymentService.FulfilOrderAsync(request.OrderId, CancellationToken.None);

        response.OrderId = request.OrderId;
        response.PayPalCaptureId = payment.PayPalCaptureId ?? string.Empty;
        response.CaptureStatus = payment.CaptureStatus ?? payment.Status.ToString();
        response.CapturedAmount = payment.CapturedAmount;
        response.PayPalFeeAmount = payment.PayPalFeeAmount;
        response.NetAmount = payment.NetAmount;

        return Results.Ok(response);
    }
}
