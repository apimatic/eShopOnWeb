using System.Linq;
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
/// List the signed in user's subscriptions. A user who has never subscribed gets an empty list, not
/// an error.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(user, subscriptionService, cancellationToken);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionService subscriptionService)
        => HandleAsync(new ClaimsPrincipal(), subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        var response = new MySubscriptionsResponse();

        var subscriptions = await subscriptionService.ListMySubscriptionsAsync(user.ToSubscriptionActor(),
            cancellationToken);

        response.Subscriptions.AddRange(subscriptions.Select(subscription => subscription.ToDto()));

        return Results.Ok(response);
    }
}
