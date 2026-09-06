using System.Linq;
using System.Security.Claims;
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
/// Lists the subscriptions held by the authenticated shopper, newest first.
/// </summary>
/// <remarks>
/// A shopper who has never subscribed has no billing customer, and gets an empty list rather than
/// an error.
/// </remarks>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, SubscriberProfile, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user,
                ISubscriberProfileResolver profileResolver,
                ISubscriptionService subscriptionService) =>
            {
                var subscriber = await profileResolver.ResolveAsync(user);
                if (subscriber is null)
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(subscriber, subscriptionService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriberProfile subscriber, ISubscriptionService subscriptionService)
    {
        var subscriptions = await subscriptionService.ListSubscriptionsAsync(subscriber);

        var response = new ListMySubscriptionsResponse
        {
            Subscriptions = subscriptions.Select(subscription => subscription.ToDto()).ToList()
        };

        return Results.Ok(response);
    }
}
