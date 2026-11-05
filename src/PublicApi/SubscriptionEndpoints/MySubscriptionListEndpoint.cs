using System.Linq;
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

/// <summary>
/// Lists the authenticated user's subscriptions
/// </summary>
public class MySubscriptionListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionService subscriptionService,
                UserManager<ApplicationUser> userManager, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(user, subscriptionService, userManager, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, ISubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager, CancellationToken cancellationToken)
    {
        var subscriber = await SubscriberResolver.ResolveAsync(user, userManager);
        if (subscriber is null) return Results.Unauthorized();

        var response = new ListMySubscriptionsResponse();
        var subscriptions = await subscriptionService.ListSubscriptionsAsync(subscriber, cancellationToken);
        response.Subscriptions.AddRange(subscriptions.Select(SubscriptionDto.From));
        return Results.Ok(response);
    }
}
