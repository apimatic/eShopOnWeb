using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Operator action: marks the order fulfilled and captures the money. A stale hold is renewed
/// before capture; one that can no longer be renewed reports an operator-actionable error.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, int, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service) =>
                await HandleAsync(orderId, service))
            .Produces<FulfilOrderResponse>()
            .WithTags("PaymentEndpoints");
    }

    public Task<IResult> HandleAsync(int orderId, IOrderPaymentService service) =>
        PaymentApiHelpers.RunAsync(async () =>
        {
            var outcome = await service.FulfilAsync(orderId);

            var response = new FulfilOrderResponse
            {
                OrderId = orderId,
                PaymentStatus = outcome.PaymentStatus.ToString(),
                CaptureId = outcome.CaptureId,
                CaptureStatus = outcome.CaptureStatus,
                CapturedAmount = outcome.CapturedAmount,
                PayPalFee = outcome.PayPalFee,
                NetAmount = outcome.NetAmount,
                Currency = outcome.CurrencyCode
            };
            return Results.Ok(response);
        });
}
