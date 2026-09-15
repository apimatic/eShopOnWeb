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

/// <summary>
/// Saves (vaults) a card for the signed-in shopper. The response identifies the saved card and describes it safely
/// (brand, last four, expiry) — never full card details. Returns the payment method id as a top-level field.
/// </summary>
public class SaveCardEndpoint : IEndpoint<IResult, SaveCardRequest>
{
    private readonly ISavedCardService _savedCards;

    public SaveCardEndpoint(ISavedCardService savedCards)
    {
        _savedCards = savedCards;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SaveCardRequest request, ClaimsPrincipal user) =>
            {
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request);
            })
            .Produces<SaveCardResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SaveCardRequest request)
    {
        var savedCard = await _savedCards.SaveCardAsync(request.BuyerId, request.Card.ToCardDetails(), request.Label);
        return Results.Created($"api/payment-methods/{savedCard.Id}",
            new SaveCardResponse(savedCard.Id, savedCard.ToDto()));
    }
}
