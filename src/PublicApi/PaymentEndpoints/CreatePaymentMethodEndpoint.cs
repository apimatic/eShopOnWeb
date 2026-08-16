using System.Security.Claims;
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
/// POST /api/payment-methods — saves (vaults) a card for the signed-in shopper. The response
/// identifies the saved card safely (brand + last four + expiry) — never full card details.
/// Returns the payment method id as a top-level field.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodEndpoint.Request, ISavedPaymentMethodService>
{
    public record Request(string BuyerId, CardModel Card);

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CardModel card, ClaimsPrincipal user, ISavedPaymentMethodService savedCardService) =>
                await HandleAsync(new Request(user.GetBuyerId(), card), savedCardService))
            .Produces<SavedCardDto>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(Request request, ISavedPaymentMethodService savedCardService)
    {
        var saved = await savedCardService.SaveCardAsync(request.BuyerId, request.Card.ToDetails());
        var dto = PaymentDtoMapper.ToDto(saved);
        return Results.Created($"api/payment-methods/{dto.PaymentMethodId}", dto);
    }
}
