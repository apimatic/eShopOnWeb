using System.Linq;
using System.Security.Claims;
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
/// Lists the authenticated shopper's Maxio subscriptions, read live from Maxio (the system of
/// record for plan, price, state, and billing dates).
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, IMaxioSubscriptionService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioSubscriptionService subscriptionService, ClaimsPrincipal user) =>
            {
                return await HandleAsync(subscriptionService, user);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioSubscriptionService subscriptionService, ClaimsPrincipal user)
    {
        var buyerId = user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var subscriptions = await subscriptionService.GetMySubscriptionsAsync(buyerId);

        var response = new ListMySubscriptionsResponse();
        response.Subscriptions.AddRange(subscriptions.Select(subscription => new SubscriptionDto
        {
            MaxioSubscriptionId = subscription.Id,
            PlanHandle = subscription.ProductHandle,
            PlanName = subscription.ProductName,
            Price = subscription.ProductPriceInCents.HasValue ? subscription.ProductPriceInCents.Value / 100m : null,
            State = subscription.State,
            NextBillingDate = subscription.NextAssessmentAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
        }));

        return Results.Ok(response);
    }
}
