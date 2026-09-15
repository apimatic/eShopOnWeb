using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Removes one of the signed-in shopper's saved cards. Afterwards it no longer appears among their cards and can no
/// longer be used to pay. A shopper can only delete their own card.
/// </summary>
public class DeleteCardEndpoint : IEndpoint<IResult, (int PaymentMethodId, ClaimsPrincipal User)>
{
    private readonly ISavedCardService _savedCards;

    public DeleteCardEndpoint(ISavedCardService savedCards)
    {
        _savedCards = savedCards;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user) => await HandleAsync((paymentMethodId, user)))
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync((int PaymentMethodId, ClaimsPrincipal User) request)
    {
        var buyerId = request.User.GetBuyerId();
        await _savedCards.DeleteCardAsync(buyerId, request.PaymentMethodId);
        return Results.NoContent();
    }
}
