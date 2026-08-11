using System.Linq;
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

/// <summary>Lists the caller's saved cards (safe descriptions only).</summary>
public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ClaimsPrincipal user,
                ISavedCardService savedCardService,
                CancellationToken ct) =>
            {
                var buyerId = user.BuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var cards = await savedCardService.ListCardsAsync(buyerId, ct);
                var response = new ListPaymentMethodsResponse
                {
                    PaymentMethods = cards.Select(c => c.ToDto()).ToList()
                };
                return Results.Ok(response);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }
}
