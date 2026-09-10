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
/// Returns the authenticated user's subscriptions. Empty when the user has never subscribed.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                var request = new MySubscriptionsRequest
                {
                    Subscriber = SubscriberIdentityFactory.FromPrincipal(user),
                    CancellationToken = cancellationToken
                };
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, ISubscriptionService subscriptionService)
    {
        if (request.Subscriber is null)
        {
            return Results.Unauthorized();
        }

        var response = new MySubscriptionsResponse(request.CorrelationId());
        var subscriptions = await subscriptionService.GetSubscriptionsAsync(request.Subscriber, request.CancellationToken);
        response.Subscriptions = subscriptions.Select(s => s.ToDto()).ToList();
        return Results.Ok(response);
    }
}
