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
/// Lists the authenticated shopper's subscriptions, newest first.
/// </summary>
/// <remarks>
/// Read straight from the billing system on every call rather than from a local mirror, so what a shopper
/// sees reflects renewals, dunning and cancellations the billing system performed on its own.
/// </remarks>
public class MySubscriptionListEndpoint : IEndpoint<IResult, MySubscriptionsQuery, ISubscriptionBillingService, SubscriberResolver>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext,
             ISubscriptionBillingService billing,
             SubscriberResolver subscribers,
             CancellationToken cancellationToken) =>
            {
                var query = new MySubscriptionsQuery(httpContext.User, cancellationToken);
                return await HandleAsync(query, billing, subscribers);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        MySubscriptionsQuery query,
        ISubscriptionBillingService billing,
        SubscriberResolver subscribers)
    {
        var subscriber = await subscribers.ResolveAsync(query.Caller);

        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        var response = new ListMySubscriptionsResponse();

        var subscriptions = await billing.ListSubscriptionsAsync(subscriber, query.CancellationToken);
        response.Subscriptions.AddRange(subscriptions.Select(SubscriptionMapping.ToDto));

        return Results.Ok(response);
    }
}

/// <summary>
/// What the my-subscriptions endpoint needs from the HTTP request.
/// </summary>
/// <param name="Caller">The authenticated caller; the only source of the shopper's identity.</param>
/// <param name="CancellationToken">Cancelled when the client disconnects.</param>
public sealed record MySubscriptionsQuery(ClaimsPrincipal Caller, CancellationToken CancellationToken);
