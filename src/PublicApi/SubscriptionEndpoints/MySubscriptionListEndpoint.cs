using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MySubscriptionListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", async (ClaimsPrincipal principal,
                ISubscriptionService subscriptionService, UserManager<ApplicationUser> userManager,
                CancellationToken cancellationToken) =>
                await HandleAsync(principal, subscriptionService, userManager, cancellationToken))
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
            })
            .Produces<MySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public static async Task<IResult> HandleAsync(ClaimsPrincipal principal,
        ISubscriptionService subscriptionService, UserManager<ApplicationUser> userManager,
        CancellationToken cancellationToken)
    {
        var username = principal.Identity?.Name;
        var user = string.IsNullOrWhiteSpace(username) ? null : await userManager.FindByNameAsync(username);
        if (user == null)
        {
            return Results.Unauthorized();
        }

        var subscriptions = await subscriptionService.ListForUserAsync(user, cancellationToken);
        return Results.Ok(new MySubscriptionsResponse { Subscriptions = subscriptions });
    }
}
