using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Finix;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Removes one of the caller's saved cards: it is disabled at Finix and deleted locally, so it can
/// neither be listed nor used to pay.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string paymentMethodId, ClaimsPrincipal user, FinixPaymentService payments, CancellationToken ct) =>
            {
                var shopperId = user.Identity?.Name ?? string.Empty;
                var response = await payments.DeleteCardAsync(shopperId, paymentMethodId, ct);
                return Results.Ok(response);
            })
            .Produces<DeletePaymentMethodResponse>()
            .WithTags("PaymentMethods");
    }
}
