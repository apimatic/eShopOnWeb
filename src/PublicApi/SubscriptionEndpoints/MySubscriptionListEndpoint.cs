using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated shopper's subscriptions, read live from the billing system of record.
/// </summary>
public class MySubscriptionListEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal caller,
             UserManager<ApplicationUser> userManager,
             ISubscriptionBillingService billingService,
             CancellationToken cancellationToken) =>
            {
                var subscriber = await SubscriberIdentityResolver.ResolveAsync(caller, userManager);
                if (subscriber is null)
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(new ListMySubscriptionsRequest(subscriber, cancellationToken), billingService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, ISubscriptionBillingService billingService)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId())
        {
            UserName = request.Subscriber.UserName
        };

        var subscriptions = await billingService.GetSubscriptionsAsync(request.Subscriber, request.CancellationToken);

        response.Subscriptions.AddRange(subscriptions.Select(subscription => subscription.ToDto()));
        response.LiveCount = response.Subscriptions.Count(subscription => subscription.IsLive);

        return Results.Ok(response);
    }
}
