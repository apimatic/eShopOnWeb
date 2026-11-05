using System.Linq;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated shopper's subscriptions
/// </summary>
public class MySubscriptionListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, SubscriberContext context, CancellationToken cancellationToken) =>
            {
                var subscriber = await context.ResolveAsync(user);
                if (subscriber is null)
                    return Results.Unauthorized();

                var subscriptions = await context.Billing.ListSubscriptionsAsync(subscriber, cancellationToken);
                return Results.Ok(new ListMySubscriptionsResponse
                {
                    Subscriptions = subscriptions.Select(s => s.ToDto()).ToList()
                });
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }
}
