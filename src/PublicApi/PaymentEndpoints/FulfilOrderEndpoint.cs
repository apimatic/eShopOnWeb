using System.Threading;
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
/// POST /api/orders/{orderId}/fulfil — operator action: captures the held funds. A stale hold is
/// renewed first; one that can no longer be renewed is reported in operator-actionable terms.
/// Afterwards the payment shows the captured amount, PayPal's fee and the net proceeds.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                IPaymentService paymentService,
                CancellationToken cancellationToken) =>
            {
                var payment = await paymentService.FulfilAsync(orderId, cancellationToken);
                return Results.Ok(new { orderId, payment = PaymentStateDto.From(payment) });
            })
            .Produces(StatusCodes.Status200OK)
            .WithTags("PaymentEndpoints");
    }
}
