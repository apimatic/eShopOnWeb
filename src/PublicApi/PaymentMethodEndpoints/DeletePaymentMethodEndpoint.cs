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
/// Removes one of the caller's saved cards. Afterwards it no longer appears among their cards and can
/// no longer be used to pay (it is deleted from PayPal's vault too).
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int paymentMethodId,
                ClaimsPrincipal user,
                ISavedCardService savedCardService,
                CancellationToken ct) =>
            {
                var buyerId = user.BuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var removed = await savedCardService.RemoveCardAsync(buyerId, paymentMethodId, ct);
                return removed ? Results.NoContent() : Results.NotFound();
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("PaymentMethodEndpoints");
    }
}
