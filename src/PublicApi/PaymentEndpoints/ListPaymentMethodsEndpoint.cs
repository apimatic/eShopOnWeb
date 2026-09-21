using System.Collections.Generic;
using System.Security.Claims;
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

/// <summary>Lists the caller's saved cards (safe descriptors only).</summary>
public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ClaimsPrincipal user,
                ISavedCardService service,
                CancellationToken ct) =>
            {
                var buyerId = PaymentEndpointHelpers.GetBuyerId(user);
                var cards = await service.GetCardsAsync(buyerId, ct);
                return Results.Ok(cards);
            })
            .Produces<IReadOnlyList<SavedCardView>>()
            .WithTags("PaymentMethodEndpoints");
    }
}
