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
/// Lists the authenticated user's subscriptions. A user with no billing customer record simply has
/// none, which is not an error.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(SubscriptionUser.ReferenceOf(user), subscriptionService, cancellationToken);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionService subscriptionService)
    {
        return Task.FromResult(Results.Unauthorized());
    }

    public async Task<IResult> HandleAsync(string userReference,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        var response = new MySubscriptionsResponse();

        var subscriptions = await subscriptionService.ListSubscriptionsAsync(userReference, cancellationToken);
        response.Subscriptions.AddRange(subscriptions.Select(SubscriptionDto.FromSubscription));

        return Results.Ok(response);
    }
}
