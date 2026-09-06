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
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List the signed-in shopper's subscriptions.
/// </summary>
public class MySubscriptionListEndpoint : IEndpoint<IResult, SubscriberIdentity, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                var subscriber = SubscriberIdentityFactory.FromPrincipal(user);

                return subscriber is null
                    ? Results.Unauthorized()
                    : await HandleAsync(subscriber, subscriptionService, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscriberIdentity subscriber, ISubscriptionService subscriptionService) =>
        HandleAsync(subscriber, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        SubscriberIdentity subscriber, ISubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        var response = new ListMySubscriptionsResponse();

        var subscriptions = await subscriptionService.ListSubscriptionsAsync(subscriber, cancellationToken);

        response.Subscriptions.AddRange(subscriptions.Select(SubscriptionDto.From));
        response.LiveCount = response.Subscriptions.Count(s => s.IsLive);

        return Results.Ok(response);
    }
}
