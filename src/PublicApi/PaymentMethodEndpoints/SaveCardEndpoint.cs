using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SaveCardRequest
{
    public CardModel Card { get; set; } = new();
}

public class SaveCardResponse
{
    public int PaymentMethodId { get; set; }
    public SavedCardDto PaymentMethod { get; set; } = new();
}

/// <summary>
/// POST /api/payment-methods — vaults a card for the signed-in shopper. The response identifies the
/// saved card and describes it safely (brand, last four, expiry) — never full card details.
/// </summary>
public class SaveCardEndpoint : IEndpoint<IResult, SaveCardRequest, ISavedCardService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SaveCardRequest request, ISavedCardService savedCardService, ClaimsPrincipal user) =>
                await HandleAsync(request, savedCardService, user))
            .Produces<SaveCardResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SaveCardRequest request, ISavedCardService savedCardService, ClaimsPrincipal user)
    {
        var ownerId = CallerIdentity.BuyerId(user);

        if (request.Card is null)
            throw new PaymentException("Card details are required to save a payment method.", PaymentErrorKind.Validation);

        var savedCard = await savedCardService.SaveCardAsync(ownerId, request.Card.ToCardDetails());
        var dto = PaymentMapper.ToDto(savedCard);

        return Results.Created($"api/payment-methods/{dto.PaymentMethodId}",
            new SaveCardResponse { PaymentMethodId = dto.PaymentMethodId, PaymentMethod = dto });
    }
}
