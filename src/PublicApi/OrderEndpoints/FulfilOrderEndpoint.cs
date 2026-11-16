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
/// Operator action: captures the authorized order total and records Finix's fee and net proceeds.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService paymentService) =>
            {
                return await HandleAsync(new FulfilOrderRequest { OrderId = orderId }, paymentService);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, IPaymentService paymentService)
    {
        var summary = await paymentService.FulfilOrderAsync(request.OrderId);
        var payment = summary.Payment;

        return Results.Ok(new FulfilOrderResponse
        {
            OrderId = summary.OrderId,
            Status = summary.Status,
            PaymentStatus = payment?.Status,
            CaptureTransferId = payment?.CaptureTransferId,
            CaptureState = payment?.CaptureState,
            CapturedAmountCents = payment?.CapturedAmountCents,
            FeeCents = payment?.FeeCents,
            NetCents = payment?.NetCents,
            FeePending = payment?.FeePending ?? false,
            Order = summary
        });
    }
}
