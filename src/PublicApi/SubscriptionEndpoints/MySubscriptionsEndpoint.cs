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
/// Lists the authenticated shopper's subscriptions
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, GetMySubscriptionsRequest, SubscriberContext, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, UserManager<ApplicationUser> userManager,
                ISubscriptionBillingService billing, CancellationToken cancellationToken) =>
            {
                var subscriber = await SubscriberContext.ResolveAsync(user, userManager, cancellationToken);
                if (subscriber is null)
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(new GetMySubscriptionsRequest(), subscriber, billing);
            })
            .Produces<GetMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(GetMySubscriptionsRequest request, SubscriberContext subscriber, ISubscriptionBillingService billing)
    {
        var response = new GetMySubscriptionsResponse(request.CorrelationId());

        var result = await billing.GetSubscriptionsAsync(subscriber.Subscriber, subscriber.RequestAborted);

        response.Subscriptions.AddRange(result.Subscriptions.Select(SubscriptionDto.From));
        response.HasPendingSubscription = result.HasPendingSubscription;
        return Results.Ok(response);
    }
}
