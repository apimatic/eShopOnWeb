using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<CreateSubscriptionResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    private static async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager,
        ISubscriptionService subscriptions,
        CancellationToken cancellationToken)
    {
        var shopper = await ShopperIdentityResolver.ResolveAsync(principal, userManager);
        if (shopper is null)
        {
            return Results.Unauthorized();
        }

        var subscription = await subscriptions.SubscribeAsync(shopper, request.ProductHandle, cancellationToken);
        return Results.Ok(new CreateSubscriptionResponse { Subscription = subscription.ToDto() });
    }
}
