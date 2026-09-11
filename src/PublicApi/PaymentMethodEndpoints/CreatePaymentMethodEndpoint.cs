using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves a card for the signed-in shopper (vaulted with PayPal). The response identifies the saved
/// card and describes it safely — never full card details.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ClaimsPrincipal, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ClaimsPrincipal user, ISavedCardService savedCardService) =>
                await HandleAsync(request, user, savedCardService))
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ClaimsPrincipal user, ISavedCardService savedCardService)
    {
        var buyerId = user.GetBuyerId();

        if (request.Card is null)
            throw new InvalidRequestException("Card details are required to save a card.");

        var savedCard = await savedCardService.SaveCardAsync(buyerId, request.Card.ToPayPalCard());
        var dto = SavedCardDto.From(savedCard);

        return Results.Created($"api/payment-methods/{dto.PaymentMethodId}", new CreatePaymentMethodResponse
        {
            PaymentMethodId = dto.PaymentMethodId,
            Card = dto
        });
    }
}

public class CreatePaymentMethodRequest
{
    public CardDetailsDto? Card { get; set; }
}

public class CreatePaymentMethodResponse
{
    /// <summary>The identifier of the saved card.</summary>
    public int PaymentMethodId { get; set; }
    public SavedCardDto Card { get; set; } = new();
}
