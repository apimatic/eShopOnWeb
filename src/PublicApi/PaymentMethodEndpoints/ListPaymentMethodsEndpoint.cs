using System.Linq;
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

/// <summary>Returns the authenticated shopper's saved cards (safe descriptions only).</summary>
public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IPaymentMethodService paymentMethodService, ClaimsPrincipal user, CancellationToken cancellationToken) =>
                await HandleAsync(paymentMethodService, user, cancellationToken))
            .Produces<ListPaymentMethodsResponse>(StatusCodes.Status200OK)
            .WithTags("PaymentMethodEndpoints");
    }

    private static async Task<IResult> HandleAsync(
        IPaymentMethodService paymentMethodService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var ownerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(ownerId))
        {
            return Results.Unauthorized();
        }

        var cards = await paymentMethodService.ListForOwnerAsync(ownerId, cancellationToken);

        return Results.Ok(new ListPaymentMethodsResponse
        {
            PaymentMethods = cards.Select(PaymentMethodDto.FromEntity).ToList()
        });
    }
}
