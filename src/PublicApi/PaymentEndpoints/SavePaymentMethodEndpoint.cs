using System.Linq;
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
/// Saves (vaults) a card for the signed-in shopper. The response describes the card safely —
/// never full card details — and the number is never stored in this application's database.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodApiRequest, ISavedCardService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SavePaymentMethodEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodApiRequest request, ISavedCardService savedCardService) =>
                await HandleAsync(request, savedCardService))
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethods");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodApiRequest request, ISavedCardService savedCardService)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.BuyerId();

        if (request.Card is null || string.IsNullOrWhiteSpace(request.Card.Number))
        {
            return Results.BadRequest(new { message = "Card details are required to save a payment method." });
        }

        var paymentMethodId = await savedCardService.SaveCardAsync(buyerId!, request.Card.ToCardDetails());

        var cards = await savedCardService.GetCardsAsync(buyerId!);
        var saved = cards.FirstOrDefault(c => c.PaymentMethodId == paymentMethodId);

        return Results.Created($"api/payment-methods/{paymentMethodId}", new SavePaymentMethodResponse
        {
            PaymentMethodId = paymentMethodId,
            Brand = saved?.Brand ?? string.Empty,
            Last4 = saved?.Last4 ?? string.Empty,
            Expiry = saved?.Expiry ?? string.Empty,
            Descriptor = saved?.Descriptor ?? string.Empty
        });
    }
}
