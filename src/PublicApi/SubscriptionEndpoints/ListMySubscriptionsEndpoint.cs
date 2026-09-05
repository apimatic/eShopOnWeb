using System.Linq;
using System.Security.Claims;
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
/// Lists the authenticated shopper's Maxio subscriptions.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal claimsPrincipal, UserManager<ApplicationUser> userManager, IMaxioSubscriptionService maxioSubscriptionService) =>
            {
                var username = claimsPrincipal.Identity?.Name;
                if (string.IsNullOrEmpty(username))
                {
                    return Results.Unauthorized();
                }

                var user = await userManager.FindByNameAsync(username);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var request = new ListMySubscriptionsRequest { UserReference = user.Id };
                return await HandleAsync(request, maxioSubscriptionService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, IMaxioSubscriptionService maxioSubscriptionService)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        var subscriptions = await maxioSubscriptionService.GetSubscriptionsForUserAsync(request.UserReference);
        response.Subscriptions = subscriptions.Select(subscription => new SubscriptionDto
        {
            Id = subscription.Id,
            PlanHandle = subscription.Plan?.Handle ?? string.Empty,
            PlanName = subscription.Plan?.Name ?? string.Empty,
            Price = (subscription.Plan?.PriceInCents ?? 0) / 100m,
            State = subscription.State,
            NextBillingDate = subscription.CurrentPeriodEndsAt
        }).ToList();

        return Results.Ok(response);
    }
}
