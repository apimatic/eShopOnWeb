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
/// List the authenticated shopper's subscriptions (plan, price, state, next billing date)
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, Subscriber, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, UserManager<ApplicationUser> userManager,
                ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
            {
                var subscriber = await SubscriberResolver.ResolveAsync(user, userManager);
                if (subscriber is null)
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(subscriber, billingService, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(Subscriber subscriber, ISubscriptionBillingService billingService) =>
        HandleAsync(subscriber, billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(Subscriber subscriber, ISubscriptionBillingService billingService, CancellationToken cancellationToken)
    {
        var response = new ListMySubscriptionsResponse();

        var subscriptions = await billingService.GetSubscriptionsAsync(subscriber, cancellationToken);
        response.Subscriptions.AddRange(subscriptions.Select(SubscriptionDto.From));

        return Results.Ok(response);
    }
}
