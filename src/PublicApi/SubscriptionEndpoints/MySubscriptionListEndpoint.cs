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
/// Lists the signed-in shopper's subscriptions.
/// </summary>
public class MySubscriptionListEndpoint : IEndpoint<IResult, ClaimsPrincipal, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(user, billingService, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ClaimsPrincipal user, ISubscriptionBillingService billingService) =>
        HandleAsync(user, billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, ISubscriptionBillingService billingService,
        CancellationToken cancellationToken)
    {
        var subscriber = SubscriberIdentityFactory.From(user);
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        var subscriptions = await billingService.GetSubscriptionsAsync(subscriber, cancellationToken);
        var dtos = subscriptions.Select(subscription => subscription.ToDto()).ToList();

        var response = new ListMySubscriptionsResponse
        {
            UserName = subscriber.UserName,
            Subscriptions = dtos,
            ActiveSubscriptions = dtos.Where(subscription => subscription.IsActive).ToList()
        };

        return Results.Ok(response);
    }
}
