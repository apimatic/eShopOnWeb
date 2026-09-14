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
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the Maxio subscriptions belonging to the authenticated shopper.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                MaxioSubscriptionService subscriptionService,
                UserManager<ApplicationUser> userManager,
                ClaimsPrincipal principal,
                CancellationToken cancellationToken) =>
            {
                return await HandleAsync(subscriptionService, userManager, principal, cancellationToken);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        MaxioSubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var user = await CreateSubscriptionEndpoint.ResolveUserAsync(principal, userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var subscriptions = await subscriptionService.ListMySubscriptionsAsync(user, cancellationToken);

        var response = new MySubscriptionsResponse
        {
            Subscriptions = new System.Collections.Generic.List<SubscriptionDto>(subscriptions)
        };

        return Results.Ok(response);
    }
}
