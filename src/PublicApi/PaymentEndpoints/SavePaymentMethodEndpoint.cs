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
/// Saves a card for the signed-in shopper (vaulted at PayPal). The response describes the card
/// safely enough to recognise it — brand, last four, expiry — never full card details.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, ISavedCardService savedCardService) =>
            {
                request.CallerBuyerId = user.GetBuyerId();
                return await HandleAsync(request, savedCardService);
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, ISavedCardService savedCardService)
    {
        var card = await savedCardService.SaveCardAsync(
            request.CallerBuyerId, request.Card.ToCardDetails(), request.Alias);

        var response = new SavePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = card.Id,
            Brand = card.Brand,
            Last4 = card.LastDigits,
            Expiry = card.Expiry,
            Alias = card.Alias
        };
        return Results.Created($"api/payment-methods/{card.Id}", response);
    }
}
