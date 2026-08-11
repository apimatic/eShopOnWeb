using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Saves (vaults) a card for the signed-in shopper. The response identifies the saved card and
/// describes it safely (brand + last four digits) — never full card details. POST /api/payment-methods
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, CardDto, ClaimsPrincipal>
{
    private readonly IPaymentMethodService _service;

    public SavePaymentMethodEndpoint(IPaymentMethodService service) => _service = service;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CardDto request, ClaimsPrincipal user) => await HandleAsync(request, user))
            .Produces<SavePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CardDto request, ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var saved = await _service.SaveCardAsync(buyerId, request.ToCardDetails());

        var response = new SavePaymentMethodResponse
        {
            PaymentMethodId = saved.Id,
            CardBrand = saved.CardBrand,
            LastFourDigits = saved.LastFourDigits,
            Expiry = saved.CardExpiry,
            CardholderName = saved.CardholderName
        };
        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}
