using Microsoft.AspNetCore.Builder;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's subscriptions
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal principal, UserManager<ApplicationUser> userManager,
                ISubscriptionService subscriptions, CancellationToken ct) =>
            {
                var subscriber = await SubscriberResolver.ResolveAsync(principal, userManager);
                if (subscriber == null) return Results.Unauthorized();

                var result = await subscriptions.ListMySubscriptionsAsync(subscriber.UserId, ct);
                return Results.Ok(new MySubscriptionsResponse(System.Guid.NewGuid())
                {
                    Subscriptions = result.Subscriptions,
                    Count = result.Count
                });
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }
}
