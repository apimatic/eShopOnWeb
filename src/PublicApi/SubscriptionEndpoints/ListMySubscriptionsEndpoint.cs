using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the calling (JWT-authenticated) user's own subscriptions. Returns an empty list if
/// they have never subscribed.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ClaimsPrincipal, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IMaxioSubscriptionService subscriptionService) =>
            {
                return await HandleAsync(user, subscriptionService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IMaxioSubscriptionService subscriptionService)
    {
        var username = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return Results.Unauthorized();
        }

        var response = new ListMySubscriptionsResponse();

        var subscriptions = await subscriptionService.GetSubscriptionsForBuyerAsync(username);
        response.Subscriptions.AddRange(subscriptions.Select(ToDto));

        return Results.Ok(response);
    }

    private static SubscriptionDto ToDto(MaxioSubscription subscription) => new()
    {
        SubscriptionId = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.ProductHandle,
        PlanName = subscription.ProductName,
        PriceInCents = subscription.PriceInCents,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        CreatedAt = subscription.CreatedAt
    };
}
