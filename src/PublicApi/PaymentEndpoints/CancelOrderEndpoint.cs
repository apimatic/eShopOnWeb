using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Operator action: cancels an order before fulfilment, releasing the shopper's held funds so
/// no money ever moves.
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            [SwaggerOperation(Summary = "Cancel an order and release the hold (operator)", Tags = new[] { "Orders" })]
        async (int orderId, IOrderPaymentService service, CancellationToken ct) =>
                await HandleAsync(orderId, service, ct))
            .Produces<PaymentStateResponse>()
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(int orderId, IOrderPaymentService service, CancellationToken ct)
    {
        try
        {
            var payment = await service.CancelAsync(orderId, ct);
            return Results.Ok(new PaymentStateResponse(
                orderId, payment.Status.ToString(), payment.AuthorizationId, payment.CaptureId,
                payment.Amount, payment.CapturedAmount, payment.PayPalFee, payment.NetAmount, payment.CurrencyCode));
        }
        catch (Exception ex)
        {
            return PaymentProblem.ToResult(ex);
        }
    }
}
