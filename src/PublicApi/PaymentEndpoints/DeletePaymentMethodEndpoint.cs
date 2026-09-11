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
/// DELETE /api/payment-methods/{paymentMethodId} — remove a saved card. Afterwards it no longer
/// appears among the caller's saved cards and can no longer be used to pay. Scoped to the caller.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, int, ISavedCardService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ISavedCardService savedCardService, ClaimsPrincipal user) =>
                await HandleAsync(paymentMethodId, savedCardService, user))
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(int paymentMethodId, ISavedCardService savedCardService, ClaimsPrincipal user)
    {
        var buyerId = PaymentApi.BuyerId(user);
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var result = await savedCardService.DeleteCardAsync(buyerId, paymentMethodId);
        return PaymentApi.ToHttp(result, () => Results.NoContent());
    }
}
