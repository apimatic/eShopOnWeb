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
/// Lists the caller's own subscriptions, newest first, read live from the billing system of record.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ClaimsPrincipal, CancellationToken>
{
    private readonly ISubscriptionBillingService _subscriptionBilling;
    private readonly SubscriberFactory _subscriberFactory;

    public ListMySubscriptionsEndpoint(ISubscriptionBillingService subscriptionBilling, SubscriberFactory subscriberFactory)
    {
        _subscriptionBilling = subscriptionBilling;
        _subscriberFactory = subscriberFactory;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, CancellationToken cancellationToken) => await HandleAsync(http.User, cancellationToken))
            .Produces<ListMySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var subscriber = await _subscriberFactory.CreateAsync(principal);

        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        var subscriptions = await _subscriptionBilling.ListSubscriptionsAsync(subscriber, cancellationToken);

        var response = new ListMySubscriptionsResponse
        {
            Subscriptions = subscriptions.Select(subscription => subscription.ToDto()).ToList(),
        };

        return Results.Ok(response);
    }
}
