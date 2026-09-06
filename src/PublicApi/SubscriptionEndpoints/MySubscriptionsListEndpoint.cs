using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions held by the caller identified in the bearer token.
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, ClaimsPrincipal, SubscriberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal caller, SubscriberService subscribers, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(caller, subscribers, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ClaimsPrincipal caller, SubscriberService subscribers) =>
        HandleAsync(caller, subscribers, CancellationToken.None);

    public async Task<IResult> HandleAsync(ClaimsPrincipal caller, SubscriberService subscribers, CancellationToken cancellationToken)
    {
        var response = new ListMySubscriptionsResponse();

        var subscriptions = await subscribers.GetMySubscriptionsAsync(caller, cancellationToken);

        response.Subscriptions.AddRange(subscriptions.Select(subscription => subscription.ToDto()));

        return Results.Ok(response);
    }
}
