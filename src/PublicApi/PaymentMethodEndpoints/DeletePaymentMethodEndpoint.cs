using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Removes one of the signed-in shopper's saved cards, from the app and from PayPal's vault.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, int, ClaimsPrincipal>
{
    private readonly ISavedCardService _savedCardService;

    public DeletePaymentMethodEndpoint(ISavedCardService savedCardService)
    {
        _savedCardService = savedCardService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user) =>
            {
                return await HandleAsync(paymentMethodId, user);
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(int paymentMethodId, ClaimsPrincipal user)
    {
        var buyerId = user.GetBuyerId();
        if (buyerId is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            await _savedCardService.DeleteSavedCardAsync(buyerId, paymentMethodId, CancellationToken.None);
        }
        catch (OrderStateException)
        {
            // Another shopper's card, or an unknown id: indistinguishable, both "not found".
            return Results.NotFound();
        }

        return Results.NoContent();
    }
}
