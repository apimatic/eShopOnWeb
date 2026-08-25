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
/// Saves a card to PayPal's vault for the signed-in shopper. The raw card number is never stored
/// by this application — only PayPal's vault token id and safe-to-display descriptors are kept.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedCardService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ClaimsPrincipal user, ISavedCardService savedCardService,
                CancellationToken ct) =>
            {
                request.BuyerId = user.Identity!.Name!;
                return await HandleAsync(request, savedCardService, ct);
            })
            .Produces<CreatePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedCardService savedCardService,
        CancellationToken ct)
    {
        var response = new CreatePaymentMethodResponse(request.CorrelationId());

        var card = new CardDetails(request.Card.Number, request.Card.Expiry, request.Card.SecurityCode,
            request.Card.CardholderName, request.Card.AddressLine1, request.Card.AddressLine2, request.Card.City,
            request.Card.State, request.Card.PostalCode, request.Card.CountryCode);

        var paymentMethod = await savedCardService.SaveCardAsync(request.BuyerId, card, ct);

        response.PaymentMethodId = paymentMethod.Id;
        response.Brand = paymentMethod.Brand;
        response.LastDigits = paymentMethod.LastDigits;
        response.Expiry = paymentMethod.Expiry;
        return Results.Created($"api/payment-methods/{paymentMethod.Id}", response);
    }
}
