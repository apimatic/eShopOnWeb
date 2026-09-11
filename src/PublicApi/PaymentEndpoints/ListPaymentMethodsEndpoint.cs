using System.Linq;
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

/// <summary>GET /api/payment-methods — the caller's own saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ISavedCardService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISavedCardService savedCardService, ClaimsPrincipal user) => await HandleAsync(savedCardService, user))
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ISavedCardService savedCardService, ClaimsPrincipal user)
    {
        var buyerId = PaymentApi.BuyerId(user);
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var result = await savedCardService.ListCardsAsync(buyerId);
        return PaymentApi.ToHttp(result, cards => new { paymentMethods = cards.Select(PaymentApi.SavedCardDto).ToArray() });
    }
}
