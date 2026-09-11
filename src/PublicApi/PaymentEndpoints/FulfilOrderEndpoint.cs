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
/// Operator action: marks the order fulfilled, which is when the held money is actually
/// captured. A hold that has gone stale is renewed rather than failing the capture outright.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            [SwaggerOperation(Summary = "Fulfil an order and capture payment (operator)", Tags = new[] { "Orders" })]
        async (int orderId, IOrderPaymentService service, CancellationToken ct) =>
                await HandleAsync(orderId, service, ct))
            .Produces<PaymentStateResponse>()
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(int orderId, IOrderPaymentService service, CancellationToken ct)
    {
        try
        {
            var payment = await service.FulfilAsync(orderId, ct);
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
