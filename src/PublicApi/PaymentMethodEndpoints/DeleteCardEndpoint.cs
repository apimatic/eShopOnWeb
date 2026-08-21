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
/// DELETE /api/payment-methods/{paymentMethodId} — removes the caller's saved card. Afterwards it no
/// longer appears among the caller's saved cards and can no longer be used to pay.
/// </summary>
public class DeleteCardEndpoint : IEndpoint<IResult, int, ISavedCardService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ISavedCardService savedCardService, ClaimsPrincipal user) =>
                await HandleAsync(paymentMethodId, savedCardService, user))
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(int paymentMethodId, ISavedCardService savedCardService, ClaimsPrincipal user)
    {
        var ownerId = CallerIdentity.BuyerId(user);
        await savedCardService.DeleteCardAsync(ownerId, paymentMethodId);
        return Results.NoContent();
    }
}
