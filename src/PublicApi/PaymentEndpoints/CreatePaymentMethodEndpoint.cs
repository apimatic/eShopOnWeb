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
/// POST /api/payment-methods — save a card for the signed-in shopper (vaulted at PayPal). The
/// response identifies the saved card and describes it safely (brand + last 4), never full details.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CardDto, ISavedCardService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CardDto request, ISavedCardService savedCardService, ClaimsPrincipal user) =>
                await HandleAsync(request, savedCardService, user))
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CardDto request, ISavedCardService savedCardService, ClaimsPrincipal user)
    {
        var buyerId = PaymentApi.BuyerId(user);
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var result = await savedCardService.SaveCardAsync(buyerId, request.ToGateway());
        return PaymentApi.ToHttp(result, PaymentApi.SavedCardDto);
    }
}
