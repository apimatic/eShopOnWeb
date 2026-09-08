using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ClaimsPrincipal, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(user, subscriptionService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, ISubscriptionService subscriptionService)
    {
        string? userName = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        try
        {
            var response = new ListMySubscriptionsResponse();

            var subscriptions = await subscriptionService.ListSubscriptionsForUserAsync(userName, default);

            response.Subscriptions.AddRange(subscriptions.Select(subscription => new SubscriptionDto
            {
                Id = subscription.SubscriptionId,
                State = subscription.State,
                PlanHandle = subscription.PlanHandle,
                PlanName = subscription.PlanName,
                PriceInCents = subscription.PriceInCents,
                NextBillingDate = subscription.NextBillingDate,
                CreatedAt = subscription.CreatedAt
            }));

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return SubscriptionEndpointErrorMapping.ToErrorResult(ex);
        }
    }
}
