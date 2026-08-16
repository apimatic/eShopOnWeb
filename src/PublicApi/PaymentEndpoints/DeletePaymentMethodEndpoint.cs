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
/// DELETE /api/payment-methods/{paymentMethodId} — removes a saved card. Afterwards it no longer
/// appears among the caller's saved cards and can no longer be used to pay.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodEndpoint.Request, ISavedPaymentMethodService>
{
    public record Request(int PaymentMethodId, string BuyerId);

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user, ISavedPaymentMethodService savedCardService) =>
                await HandleAsync(new Request(paymentMethodId, user.GetBuyerId()), savedCardService))
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(Request request, ISavedPaymentMethodService savedCardService)
    {
        await savedCardService.DeleteAsync(request.BuyerId, request.PaymentMethodId);
        return Results.NoContent();
    }
}
