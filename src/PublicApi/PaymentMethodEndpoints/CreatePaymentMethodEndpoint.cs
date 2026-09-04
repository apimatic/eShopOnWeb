using System;
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
/// Saves a card for the signed-in shopper in the PayPal vault. Full card details are never
/// stored by this application; only a safe description is kept and returned.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ClaimsPrincipal, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ClaimsPrincipal user, ISavedCardService savedCardService) =>
            {
                return await HandleAsync(request, user, savedCardService);
            })
            .Produces<CreatePaymentMethodResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ClaimsPrincipal user, ISavedCardService savedCardService)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
            return Results.Unauthorized();

        var result = await savedCardService.SaveCardAsync(buyerId, ToCardDetails(request?.Card));

        var response = new CreatePaymentMethodResponse(request?.CorrelationId() ?? Guid.NewGuid())
        {
            PaymentMethodId = result.SavedCardId,
            Description = $"{result.Brand} ending {result.Last4}",
            Brand = result.Brand,
            Last4 = result.Last4,
            Expiry = result.Expiry,
            CardholderName = result.CardholderName
        };

        return Results.Created($"api/payment-methods/{result.SavedCardId}", response);
    }

    private static CardDetails ToCardDetails(CardPaymentRequest? card)
    {
        if (card is null)
            throw new ApplicationCore.Exceptions.PaymentStateException("Card details are required.");

        return new CardDetails
        {
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            BillingAddress = card.BillingAddress is null ? null : new CardBillingAddress
            {
                AddressLine1 = card.BillingAddress.AddressLine1,
                AddressLine2 = card.BillingAddress.AddressLine2,
                AdminArea1 = card.BillingAddress.AdminArea1,
                AdminArea2 = card.BillingAddress.AdminArea2,
                PostalCode = card.BillingAddress.PostalCode,
                CountryCode = card.BillingAddress.CountryCode
            }
        };
    }
}