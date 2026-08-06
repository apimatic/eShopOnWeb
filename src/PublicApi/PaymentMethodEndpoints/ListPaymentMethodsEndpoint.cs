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
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Lists the signed-in shopper's saved cards (safe descriptors only).</summary>
public class ListPaymentMethodsEndpoint
    : IEndpoint<IResult, ISavedCardService, ClaimsPrincipal, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISavedCardService savedCardService, CancellationToken ct) =>
                await HandleAsync(savedCardService, user, ct))
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ISavedCardService savedCardService, ClaimsPrincipal user,
        CancellationToken ct)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var cards = await savedCardService.ListCardsAsync(buyerId, ct);

        var response = new ListPaymentMethodsResponse
        {
            PaymentMethods = cards.Select(SavedCardDto.From).ToList()
        };
        return Results.Ok(response);
    }
}
