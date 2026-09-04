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
/// Operator action: marks an order fulfilled and captures the held funds.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService paymentService) =>
            {
                return await HandleAsync(new FulfilOrderRequest(orderId), paymentService);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, IOrderPaymentService paymentService)
    {
        var response = new FulfilOrderResponse(request.CorrelationId());

        var order = await paymentService.FulfilAsync(request.OrderId, System.Threading.CancellationToken.None);

        response.OrderId = order.Id;
        response.Status = order.Status.ToString();
        response.CaptureId = order.CaptureId;
        response.CaptureStatus = order.CaptureStatus;
        response.CaptureAmount = order.CaptureGrossAmount;
        response.PaypalFee = order.CaptureFeeAmount;
        response.NetAmount = order.CaptureNetAmount;
        response.Currency = order.Currency;

        return Results.Ok(response);
    }
}