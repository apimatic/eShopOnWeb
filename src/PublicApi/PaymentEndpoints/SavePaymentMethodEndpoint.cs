using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Saves a card for the signed-in shopper with the provider's vault. The response
/// identifies the card with display data only (brand, last four digits, expiry) -
/// full card details are never stored or returned.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, IPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SavePaymentMethodEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, IPaymentService paymentService) =>
            {
                return await HandleAsync(request, paymentService);
            })
            .Produces<SavePaymentMethodResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, IPaymentService paymentService)
    {
        var buyerId = PaymentEndpointHelpers.GetBuyerId(_httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal());
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Results.Unauthorized();
        }

        var card = PaymentEndpointHelpers.ToCardInput(request.Card, out var cardError);
        if (card == null)
        {
            return PaymentEndpointHelpers.FromError(cardError!);
        }

        var result = await paymentService.SaveCardAsync(buyerId, card, default);
        if (!result.Succeeded)
        {
            return PaymentEndpointHelpers.FromError(result.Error!);
        }

        var saved = result.Card!;
        var response = new SavePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = saved.Id,
            Brand = saved.Brand,
            LastFourDigits = saved.LastFourDigits,
            Expiry = saved.Expiry,
            CardholderName = saved.CardholderName
        };

        return Results.Created($"api/payment-methods/{response.PaymentMethodId}", response);
    }
}



