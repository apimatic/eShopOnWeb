using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List the authenticated shopper's subscriptions
/// </summary>
/// <remarks>
/// Read straight from the billing system of record on every call, so the state shown is the state
/// billing will act on. A shopper who has never subscribed gets an empty list, not an error.
/// </remarks>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, SubscriberIdentity, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext,
                SubscriberIdentityResolver identityResolver,
                ISubscriptionService subscriptionService,
                CancellationToken cancellationToken) =>
            {
                var subscriber = await identityResolver.ResolveAsync(httpContext.User);
                if (subscriber is null) return Results.Unauthorized();

                return await HandleAsync(subscriber, subscriptionService, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute(
                summary: "Lists the authenticated user's subscriptions",
                description: "Returns every subscription held by the caller, in any state, read from the billing system of record."));
    }

    public Task<IResult> HandleAsync(SubscriberIdentity subscriber, ISubscriptionService subscriptionService) =>
        HandleAsync(subscriber, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(SubscriberIdentity subscriber,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        var response = new ListMySubscriptionsResponse();

        var subscriptions = await subscriptionService.GetSubscriptionsAsync(subscriber, cancellationToken);
        response.Subscriptions.AddRange(subscriptions.Select(s => s.ToDto()));

        return Results.Ok(response);
    }
}
