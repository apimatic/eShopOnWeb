using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves a card for the signed-in shopper by vaulting it at PayPal. The response identifies the saved card
/// and describes it safely (brand + last 4) — never full card details.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, ISavedCardService savedCardService) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();
                if (request.Card is null)
                    return Results.BadRequest(new { message = "Card details are required to save a card." });
                request.BuyerId = buyerId;
                return await HandleAsync(request, savedCardService);
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, ISavedCardService savedCardService)
    {
        var card = new CardDetails(
            request.Card.Number,
            request.Card.ExpiryMonth,
            request.Card.ExpiryYear,
            request.Card.SecurityCode,
            request.Card.CardholderName,
            request.Card.BillingAddress is null ? null : new BillingAddress(
                request.Card.BillingAddress.AddressLine1,
                request.Card.BillingAddress.AddressLine2,
                request.Card.BillingAddress.City,
                request.Card.BillingAddress.State,
                request.Card.BillingAddress.PostalCode,
                request.Card.BillingAddress.CountryCode));

        var saved = await savedCardService.SaveCardAsync(request.BuyerId!, card, request.Alias);

        var response = new SavePaymentMethodResponse
        {
            PaymentMethodId = saved.Id,
            PaymentMethod = PaymentMethodDto.FromEntity(saved)
        };
        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}
