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
/// Lists the calling shopper's subscriptions, read from Maxio (the billing system of record). A subscribe request
/// whose outcome was unknown is settled here first, or reported as pending.
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, UserManager<ApplicationUser> userManager, ISubscriptionService subscriptionService,
                CancellationToken cancellationToken) =>
            {
                return await HandleAsync(user, userManager, subscriptionService, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, UserManager<ApplicationUser> userManager,
        ISubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        var subscriber = await SubscriberResolver.ResolveAsync(user, userManager);
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        var mine = await subscriptionService.GetMySubscriptionsAsync(subscriber, cancellationToken);

        var response = new ListMySubscriptionsResponse();
        response.Subscriptions.AddRange(mine.Subscriptions.Select(SubscriptionDto.From));
        if (mine.Pending is not null)
        {
            response.Pending = new PendingSubscriptionDto
            {
                PlanHandle = mine.Pending.PlanHandle,
                SubscriptionReference = mine.Pending.SubscriptionReference,
                RequestedAt = mine.Pending.RequestedAtUtc
            };
        }

        return Results.Ok(response);
    }
}
