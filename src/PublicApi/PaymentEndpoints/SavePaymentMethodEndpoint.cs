using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/payment-methods — vaults a card for the signed-in shopper. Returns the saved card id as a
/// top-level field plus a safe description (brand, last four, expiry). Full card details are never stored.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ISavedCardService service, HttpContext http) =>
            {
                request.BuyerId = http.BuyerId();
                return await HandleAsync(request, service);
            })
            .Produces<PaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, ISavedCardService service)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }
        if (request.Card is null || string.IsNullOrWhiteSpace(request.Card.Number))
        {
            throw new ValidationException("Card details are required to save a card.");
        }

        var saved = await service.SaveCardAsync(request.BuyerId, request.Card.ToCardDetails());
        var response = PaymentMethodResponse.From(saved);
        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}
