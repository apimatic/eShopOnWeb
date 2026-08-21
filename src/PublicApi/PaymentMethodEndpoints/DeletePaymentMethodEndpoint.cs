using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Removes one of the signed-in shopper's saved cards — also deleting it from the PayPal vault so it
/// can no longer be used to pay. Shopper-scoped: acts only on the caller's own card.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, IPaymentService paymentService, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var buyerId = PaymentMapping.GetBuyerId(user);
                await paymentService.DeleteCardAsync(paymentMethodId, buyerId, ct);
                return Results.NoContent();
            })
            .WithTags("PaymentMethodEndpoints");
    }
}
