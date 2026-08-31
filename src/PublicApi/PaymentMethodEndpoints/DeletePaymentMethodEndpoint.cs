using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Removes one of the caller's saved cards. Afterwards it no longer appears among the
/// caller's saved cards and can no longer be used to pay.
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
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        await _savedCardService.DeleteAsync(buyerId, paymentMethodId);
        return Results.NoContent();
    }
}
