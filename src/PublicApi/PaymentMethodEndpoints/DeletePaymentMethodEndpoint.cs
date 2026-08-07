using System.Security.Claims;
using System.Threading;
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
/// Removes one of the authenticated shopper's saved cards. Afterwards it no longer appears among the
/// caller's saved cards and can no longer be used to pay.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, IPaymentMethodService paymentMethodService, ClaimsPrincipal user, CancellationToken cancellationToken) =>
                await HandleAsync(paymentMethodId, paymentMethodService, user, cancellationToken))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("PaymentMethodEndpoints");
    }

    private static async Task<IResult> HandleAsync(
        int paymentMethodId,
        IPaymentMethodService paymentMethodService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var ownerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(ownerId))
        {
            return Results.Unauthorized();
        }

        var result = await paymentMethodService.DeleteAsync(ownerId, paymentMethodId, cancellationToken);

        return result.Outcome switch
        {
            DeleteCardOutcome.Deleted => Results.NoContent(),
            _ => Results.Problem(detail: $"Saved card {paymentMethodId} was not found.", statusCode: StatusCodes.Status404NotFound)
        };
    }
}
