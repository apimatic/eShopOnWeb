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
/// Read a single subscription. An identifier the provider does not know yields 404 rather than an
/// empty payload.
/// </summary>
public class GetSubscriptionEndpoint : IEndpoint<IResult, int, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, ISubscriptionService subscriptionService,
                CancellationToken cancellationToken) =>
            {
                return await HandleAsync(subscriptionId, user, subscriptionService, cancellationToken);
            })
            .Produces<GetSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(int subscriptionId, ISubscriptionService subscriptionService)
        => HandleAsync(subscriptionId, new ClaimsPrincipal(), subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(int subscriptionId, ClaimsPrincipal user,
        ISubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        var subscription = await subscriptionService.GetSubscriptionAsync(user.ToSubscriptionActor(),
            subscriptionId, cancellationToken);

        if (subscription is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new GetSubscriptionResponse { Subscription = subscription.ToDto() });
    }
}
