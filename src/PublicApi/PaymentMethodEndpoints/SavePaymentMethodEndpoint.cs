using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves (vaults) a card for the signed-in shopper. Returns the saved-card id as a top-level field
/// plus a safe description; never full card details.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, ISavedCardService savedCardService) =>
            {
                request ??= new SavePaymentMethodRequest();
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, savedCardService);
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, ISavedCardService savedCardService)
    {
        if (string.IsNullOrWhiteSpace(request.Number))
        {
            return Results.BadRequest("Card details are required to save a card.");
        }

        var saved = await savedCardService.SaveCardAsync(request.BuyerId, request.ToCardDetails());

        var response = new SavePaymentMethodResponse
        {
            PaymentMethodId = saved.Id,
            CardBrand = saved.CardBrand,
            Last4 = saved.Last4,
            ExpiryMonth = saved.ExpiryMonth,
            ExpiryYear = saved.ExpiryYear,
            CardholderName = saved.CardholderName
        };
        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}
