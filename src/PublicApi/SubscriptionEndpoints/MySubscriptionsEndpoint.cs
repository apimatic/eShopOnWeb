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
/// Lists the authenticated shopper's subscriptions
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, ClaimsPrincipal, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionBillingService subscriptionBillingService) =>
            {
                return await HandleAsync(user, subscriptionBillingService);
            })
           .Produces<ListMySubscriptionsResponse>()
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, ISubscriptionBillingService subscriptionBillingService)
    {
        var username = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return Results.Unauthorized();
        }

        var response = new ListMySubscriptionsResponse();

        var subscriptions = await subscriptionBillingService.ListSubscriptionsAsync(username);

        response.Subscriptions.AddRange(subscriptions.Select(s => new SubscriptionDto
        {
            SubscriptionId = s.SubscriptionId,
            PlanHandle = s.PlanHandle,
            PlanName = s.PlanName,
            PriceInCents = s.PriceInCents,
            Currency = s.Currency,
            State = s.State,
            ActivatedAt = s.ActivatedAt,
            NextBillingAt = s.NextBillingAt
        }));

        return Results.Ok(response);
    }
}
