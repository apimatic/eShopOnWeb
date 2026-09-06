using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions held by the authenticated caller.
/// </summary>
public class MySubscriptionListEndpoint : IEndpoint<IResult, ClaimsPrincipal, CancellationToken>
{
    private readonly ISubscriptionBillingService _subscriptionBillingService;

    public MySubscriptionListEndpoint(ISubscriptionBillingService subscriptionBillingService)
    {
        _subscriptionBillingService = subscriptionBillingService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(user, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var response = new ListMySubscriptionsResponse();

        var subscriber = SubscriberIdentityFactory.FromPrincipal(user);
        if (subscriber is null)
        {
            return BillingResults.Unauthenticated();
        }

        try
        {
            var subscriptions = await _subscriptionBillingService.GetSubscriptionsAsync(subscriber, cancellationToken);
            response.Subscriptions.AddRange(subscriptions.Select(subscription => subscription.ToDto()));
        }
        catch (BillingException ex)
        {
            return BillingResults.Problem(ex, response.CorrelationId());
        }

        return Results.Ok(response);
    }
}
