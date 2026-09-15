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

/// <summary>
/// GET /api/payment-methods — the caller's saved cards. Shopper-scoped: only the caller's cards are
/// ever returned.
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, string, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, ISavedCardService service) =>
                await HandleAsync(user.GetBuyerId(), service))
            .Produces<PaymentMethodListResponse>()
            .WithTags("PaymentMethods");
    }

    public async Task<IResult> HandleAsync(string buyerId, ISavedCardService service)
    {
        var cards = await service.GetCardsAsync(buyerId);
        var response = new PaymentMethodListResponse
        {
            PaymentMethods = cards.Select(c => c.ToResponse()).ToList()
        };
        return Results.Ok(response);
    }
}
