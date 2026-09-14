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
/// Lists the authenticated shopper's subscriptions. GET /api/my-subscriptions
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, SubscriberIdentity, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
            {
                var subscriber = SubscriberIdentityResolver.Resolve(user);
                if (subscriber is null)
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(subscriber, billingService, cancellationToken);
            })
            .Produces<GetMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscriberIdentity subscriber, ISubscriptionBillingService billingService)
        => HandleAsync(subscriber, billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(SubscriberIdentity subscriber, ISubscriptionBillingService billingService, CancellationToken cancellationToken)
    {
        var response = new GetMySubscriptionsResponse();

        var subscriptions = await billingService.ListSubscriptionsAsync(subscriber, cancellationToken);
        response.Subscriptions = subscriptions.Select(s => s.ToDto()).ToList();

        return Results.Ok(response);
    }
}
