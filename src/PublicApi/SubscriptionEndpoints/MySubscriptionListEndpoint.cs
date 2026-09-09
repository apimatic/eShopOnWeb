using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated caller's recurring subscriptions with their live
/// Maxio state (plan, price, state, next billing date).
/// </summary>
public class MySubscriptionListEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                return await HandleCoreAsync(user, subscriptionService);
            })
            .RequireAuthorization()
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionService subscriptionService)
    {
        return HandleCoreAsync(null, subscriptionService);
    }

    private async Task<IResult> HandleCoreAsync(ClaimsPrincipal? user, ISubscriptionService subscriptionService)
    {
        var username = user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(username))
        {
            return Results.Unauthorized();
        }

        var subscriptions = await subscriptionService.GetMySubscriptionsAsync(username, CancellationToken.None);

        var response = new ListMySubscriptionsResponse(Guid.NewGuid())
        {
            Subscriptions = subscriptions.Select(SubscriptionCreateEndpoint.ToDto).ToList()
        };

        return Results.Ok(response);
    }
}
