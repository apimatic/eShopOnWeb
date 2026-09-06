using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions held by the caller, read straight from the billing system of record so the
/// answer survives an application restart. Returns an empty list for a shopper who has never subscribed.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, Subscriber, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext,
             ISubscriptionService subscriptionService,
             UserManager<ApplicationUser> userManager,
             CancellationToken cancellationToken) =>
            {
                var subscriber = await SubscriberResolver.ResolveAsync(httpContext.User, userManager);

                return subscriber is null
                    ? Results.Unauthorized()
                    : await HandleAsync(subscriber, subscriptionService, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(Subscriber subscriber, ISubscriptionService subscriptionService) =>
        HandleAsync(subscriber, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        Subscriber subscriber,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        var response = new ListMySubscriptionsResponse();

        var subscriptions = await subscriptionService.GetSubscriptionsAsync(subscriber, cancellationToken);

        response.Subscriptions.AddRange(subscriptions.Select(subscription => subscription.ToDto()));
        response.LiveCount = subscriptions.Count(subscription => subscription.IsLive);

        return Results.Ok(response);
    }
}
