using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public record CreatePaymentMethodRequest(CardRequest Card, string? Label);

/// <summary>Identifies the saved card and describes it safely — never full card details.</summary>
public record PaymentMethodResponse(int PaymentMethodId, string Brand, string Last4, string Expiry, string? Label);

/// <summary>
/// POST /api/payment-methods — saves a card for the signed-in shopper (Flow 2). The card is vaulted
/// with PayPal; only a safe descriptor is returned. Returns the saved card id as a top-level field.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedCardService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreatePaymentMethodEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest? request, ISavedCardService savedCardService) =>
                await HandleAsync(request!, savedCardService))
            .Produces<PaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedCardService savedCardService)
    {
        var buyerId = _httpContextAccessor.GetBuyerId();
        if (request?.Card is null)
            throw new PaymentException("Card details are required to save a payment method.");

        var card = PaymentDtoMapper.ToCardDetails(request.Card);
        var saved = await savedCardService.SaveCardAsync(buyerId, card, request.Label);

        var response = new PaymentMethodResponse(saved.Id, saved.Brand, saved.Last4, saved.Expiry, saved.Label);
        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}
