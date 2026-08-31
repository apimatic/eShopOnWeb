using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves (vaults) a card for the signed-in shopper. The response identifies the saved
/// card and describes it only by safe display data - never full card details.
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
            (CreatePaymentMethodRequest request, ISavedCardService savedCardService) =>
            {
                return await HandleAsync(request, savedCardService);
            })
            .Produces<CreatePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedCardService savedCardService)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var card = new GatewayCardDetails(
            request.Card.Number,
            request.Card.Expiry,
            request.Card.SecurityCode,
            request.Card.Name,
            request.Card.BillingAddress is null
                ? null
                : new GatewayBillingAddress(
                    request.Card.BillingAddress.AddressLine1,
                    request.Card.BillingAddress.AddressLine2,
                    request.Card.BillingAddress.City,
                    request.Card.BillingAddress.State,
                    request.Card.BillingAddress.PostalCode,
                    request.Card.BillingAddress.CountryCode));

        var savedCard = await savedCardService.SaveAsync(buyerId, card);

        var response = new CreatePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = savedCard.Id,
            LastDigits = savedCard.LastDigits,
            Brand = savedCard.Brand,
            Expiry = savedCard.Expiry
        };
        return Results.Created($"api/payment-methods/{savedCard.Id}", response);
    }
}
