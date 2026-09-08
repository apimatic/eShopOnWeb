using System;
using System.Linq;
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
/// Lists the authenticated user's subscriptions in the billing system.
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionService subscriptionService, System.Security.Claims.ClaimsPrincipal user) =>
            {
                return await HandleAsync(subscriptionService, user);
            })
            .Produces<MySubscriptionsListResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    Task<IResult> IEndpoint<IResult, ISubscriptionService>.HandleAsync(ISubscriptionService subscriptionService)
    {
        throw new NotSupportedException("Use HandleAsync(ISubscriptionService, ClaimsPrincipal) instead.");
    }

    public async Task<IResult> HandleAsync(ISubscriptionService subscriptionService,
        System.Security.Claims.ClaimsPrincipal user)
    {
        var response = new MySubscriptionsListResponse(Guid.NewGuid());

        var username = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return Results.Unauthorized();
        }

        var subscriptions = await subscriptionService.GetMySubscriptionsAsync(username);

        response.Subscriptions.AddRange(subscriptions.Select(subscription => new SubscriptionSummaryDto
        {
            BillingSubscriptionId = subscription.BillingSubscriptionId,
            State = subscription.State,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            PriceInCents = subscription.PriceInCents,
            NextBillingDate = subscription.NextBillingDate,
            ActivatedAt = subscription.ActivatedAt,
            CanceledAt = subscription.CanceledAt,
            CreatedAt = subscription.CreatedAt
        }));

        return Results.Ok(response);
    }
}
