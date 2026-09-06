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
/// Lists the authenticated shopper's own subscriptions.
/// </summary>
/// <remarks>
/// Read straight from the billing system of record, so the result is correct after a restart even
/// though eShopOnWeb keeps no local subscription table.
/// </remarks>
public class MySubscriptionListEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        // Scoped services are taken as handler parameters, never constructor-injected: endpoint
        // instances are resolved once at startup, so a constructor-injected DbContext-backed service
        // would be shared by every request and fail under concurrency.
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionBillingService billingService,
             SubscriberIdentityAccessor subscriberIdentityAccessor, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(user, billingService, subscriberIdentityAccessor, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    /// <summary>
    /// Overload required by <see cref="IEndpoint{TResponse, TService}"/>. It has no principal and no
    /// identity accessor, so it can only answer 401; the route above supplies both.
    /// </summary>
    public Task<IResult> HandleAsync(ISubscriptionBillingService billingService) =>
        Task.FromResult(Results.Unauthorized());

    public async Task<IResult> HandleAsync(
        ClaimsPrincipal user,
        ISubscriptionBillingService billingService,
        SubscriberIdentityAccessor subscriberIdentityAccessor,
        CancellationToken cancellationToken)
    {
        var subscriber = await subscriberIdentityAccessor.ResolveAsync(user);
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        var response = new ListMySubscriptionsResponse();

        var subscriptions = await billingService.ListSubscriptionsAsync(subscriber, cancellationToken);
        response.Subscriptions.AddRange(subscriptions.Select(s => s.ToDto()));

        return Results.Ok(response);
    }
}
