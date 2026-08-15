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
/// Shopper action. Saves a card for the signed-in shopper (stored in PayPal's vault). Returns the
/// saved card's id as a top-level field, plus a safe description — never full card details.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, CardDto, ISavedCardService>
{
    private readonly IHttpContextAccessor _http;

    public SavePaymentMethodEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CardDto request, ISavedCardService savedCardService) =>
                await HandleAsync(request, savedCardService))
            .Produces<PaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(CardDto request, ISavedCardService savedCardService)
    {
        var buyerId = EndpointCaller.RequireBuyerId(_http);

        if (request is null || string.IsNullOrWhiteSpace(request.Number))
        {
            throw new PaymentException("Card details are required to save a card.");
        }

        var saved = await savedCardService.SaveCardAsync(buyerId, PaymentMapping.ToCardDetails(request));
        var response = PaymentMapping.ToPaymentMethodResponse(saved);

        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}
