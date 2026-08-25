using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves a card for the signed-in shopper via PayPal's vault. Full card details are never
/// stored in eShop's own database - only PayPal's vault id and display-safe metadata.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ClaimsPrincipal user, ISavedCardService savedCardService) =>
            {
                request.BuyerId = user.Identity!.Name!;
                return await HandleAsync(request, savedCardService);
            })
            .Produces<CreatePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedCardService savedCardService)
    {
        var response = new CreatePaymentMethodResponse(request.CorrelationId());

        var card = new CardDetails
        {
            Number = request.Card.Number,
            Expiry = request.Card.Expiry,
            SecurityCode = request.Card.SecurityCode,
            CardholderName = request.Card.CardholderName,
            BillingAddress = new BillingAddress
            {
                AddressLine1 = request.Card.BillingAddress.AddressLine1,
                AddressLine2 = request.Card.BillingAddress.AddressLine2,
                City = request.Card.BillingAddress.City,
                State = request.Card.BillingAddress.State,
                PostalCode = request.Card.BillingAddress.PostalCode,
                CountryCode = request.Card.BillingAddress.CountryCode
            }
        };

        var saved = await savedCardService.SaveCardAsync(request.BuyerId, card, CancellationToken.None);

        response.PaymentMethodId = saved.Id;
        response.CardBrand = saved.CardBrand;
        response.Last4 = saved.Last4;
        response.Expiry = saved.Expiry;

        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}
