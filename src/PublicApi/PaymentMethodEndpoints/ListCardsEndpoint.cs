using System.Linq;
using System.Security.Claims;
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

/// <summary>Lists the signed-in shopper's saved cards (safe fields only).</summary>
public class ListCardsEndpoint : IEndpoint<IResult, ClaimsPrincipal>
{
    private readonly ISavedCardService _savedCards;

    public ListCardsEndpoint(ISavedCardService savedCards)
    {
        _savedCards = savedCards;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user) => await HandleAsync(user))
            .Produces<ListCardsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user)
    {
        var buyerId = user.GetBuyerId();
        var cards = await _savedCards.GetCardsAsync(buyerId);
        return Results.Ok(new ListCardsResponse(cards.Select(c => c.ToDto()).ToList()));
    }
}
