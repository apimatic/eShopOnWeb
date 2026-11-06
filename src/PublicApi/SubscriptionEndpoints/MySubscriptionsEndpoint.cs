using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated caller's subscriptions
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(user, subscriptionService, cancellationToken);
            })
            .Produces<SubscriptionListResult>()
            .WithTags("SubscriptionEndpoints");
    }

    private static async Task<IResult> HandleAsync(ClaimsPrincipal user, ISubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        var userName = user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
        return Results.Ok(await subscriptionService.ListSubscriptionsAsync(userName, cancellationToken));
    }
}
