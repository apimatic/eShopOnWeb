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

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated shopper's subscriptions
/// </summary>
public class MySubscriptionListEndpoint : IEndpoint<IResult, ClaimsPrincipal, ISubscriptionService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal claimsPrincipal, ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(claimsPrincipal, subscriptionService, cancellationToken);
            })
           .Produces<ListMySubscriptionsResponse>()
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal claimsPrincipal, ISubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        var userName = claimsPrincipal.Identity?.Name;
        if (string.IsNullOrEmpty(userName))
        {
            return Results.Unauthorized();
        }

        var response = new ListMySubscriptionsResponse();

        var subscriptions = await subscriptionService.ListSubscriptionsForUserAsync(userName, cancellationToken);

        response.Subscriptions.AddRange(subscriptions);

        return Results.Ok(response);
    }
}
