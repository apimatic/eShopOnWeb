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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Lists the caller's saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ISavedCardService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISavedCardService service, ClaimsPrincipal user) =>
                await HandleAsync(service, user))
            .Produces<SavedCardListResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(ISavedCardService service, ClaimsPrincipal user)
    {
        var buyerId = CallerIdentity.Require(user);
        var cards = await service.GetCardsAsync(buyerId, CancellationToken.None);
        return Results.Ok(new SavedCardListResponse { PaymentMethods = cards.Select(SavedCardResponse.From).ToList() });
    }
}
