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
/// POST /api/orders/{orderId}/fulfil — operator action. Captures (takes) the held funds. A stale
/// authorization is renewed first; one that can no longer be renewed is reported in operator-actionable
/// terms. After capture the payment reports the captured amount, PayPal's fee and the net proceeds.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId, IPaymentService service, CancellationToken ct) =>
            {
                var view = await service.FulfilAsync(orderId, ct);
                return Results.Ok(view);
            })
            .Produces<PaymentView>()
            .WithTags("OrderPaymentEndpoints");
    }
}
