using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the caller's own subscriptions, as held by the billing system.
/// </summary>
public class MySubscriptionListEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user,
             ISubscriberResolver subscriberResolver,
             ISubscriptionService subscriptionService,
             CancellationToken cancellationToken) =>
            {
                var subscriber = await subscriberResolver.ResolveAsync(user, cancellationToken);

                return await HandleAsync(new ListMySubscriptionsRequest(subscriber, cancellationToken), subscriptionService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, ISubscriptionService subscriptionService)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        var subscriptions = await subscriptionService.ListSubscriptionsAsync(request.Subscriber, request.CancellationToken);
        response.Subscriptions.AddRange(subscriptions.Select(subscription => subscription.ToDto()));

        return Results.Ok(response);
    }
}
