using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves a card for the signed-in shopper by vaulting it with PayPal. The response identifies the
/// saved card and describes it safely (brand + last four + expiry) — never full card details.
/// Returns the <c>paymentMethodId</c> as a top-level field.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CardDto, IPaymentMethodService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreatePaymentMethodEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CardDto request, IPaymentMethodService service) => await HandleAsync(request, service))
            .Produces<SavedCardResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CardDto request, IPaymentMethodService service)
    {
        var buyerId = _httpContextAccessor.HttpContext!.User.GetBuyerId();

        if (string.IsNullOrWhiteSpace(request.CardNumber) || string.IsNullOrWhiteSpace(request.Expiry))
        {
            throw new PaymentOperationException("A card requires at least 'cardNumber' and 'expiry' (YYYY-MM).");
        }

        var card = PaymentApiMapper.ToCardDetails(request);
        var method = await service.SaveCardAsync(buyerId, card);
        var response = PaymentApiMapper.ToResponse(method);
        return Results.Created($"api/payment-methods/{method.Id}", response);
    }
}
