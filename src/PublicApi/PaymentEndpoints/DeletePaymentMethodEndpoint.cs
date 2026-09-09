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
/// Removes one of the caller's saved cards. Afterwards it no longer appears in their cards and can
/// no longer be used to pay.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, (int PaymentMethodId, string BuyerId), ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user, ISavedCardService savedCardService) =>
                await HandleAsync((paymentMethodId, user.GetBuyerId()), savedCardService))
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync((int PaymentMethodId, string BuyerId) request, ISavedCardService savedCardService)
    {
        await savedCardService.DeleteCardAsync(request.BuyerId, request.PaymentMethodId);
        return Results.NoContent();
    }
}
