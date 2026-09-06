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
/// List the caller's own subscriptions, read live from the billing system of record.
/// </summary>
/// <remarks>
/// Whose subscriptions are returned is decided by the bearer token alone - there is no way for a
/// caller to ask for someone else's.
/// </remarks>
public class MySubscriptionListEndpoint : IEndpoint<IResult, SubscriberIdentity, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionService subscriptionService,
             UserManager<ApplicationUser> userManager,
             HttpContext httpContext,
             CancellationToken cancellationToken) =>
            {
                var subscriber = await SubscriberIdentityResolver.ResolveAsync(httpContext.User, userManager);
                if (subscriber is null)
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(subscriber, subscriptionService, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscriberIdentity subscriber, ISubscriptionService subscriptionService) =>
        HandleAsync(subscriber, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        SubscriberIdentity subscriber,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        var response = new ListMySubscriptionsResponse();

        var subscriptions = await subscriptionService.GetSubscriptionsAsync(subscriber, cancellationToken);

        response.Subscriptions.AddRange(subscriptions.Select(s => s.ToDto()));
        response.ActiveCount = subscriptions.Count(s =>
            s.State is SubscriptionState.Active or SubscriptionState.Pending);

        return Results.Ok(response);
    }
}
