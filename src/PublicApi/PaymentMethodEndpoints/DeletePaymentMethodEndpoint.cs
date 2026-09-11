using System.Security.Claims;
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
/// Removes a saved card. Afterwards it no longer appears among the caller's saved cards and can no
/// longer be used to pay. Shopper-scoped: one shopper can never delete another's card.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, int, ClaimsPrincipal, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user, IPaymentMethodService service) =>
                await HandleAsync(paymentMethodId, user, service))
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(int paymentMethodId, ClaimsPrincipal user,
        IPaymentMethodService service)
    {
        var buyerId = CallerIdentity.GetBuyerId(user);
        await service.DeleteAsync(buyerId, paymentMethodId);
        return Results.NoContent();
    }
}
