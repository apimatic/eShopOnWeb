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
/// Subscribes the authenticated shopper to a plan. Idempotent: repeating the
/// call returns the existing subscription instead of creating a duplicate.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ClaimsPrincipal, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, ISubscriptionBillingService subscriptionBillingService) =>
            {
                return await HandleAsync(request, user, subscriptionBillingService);
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ClaimsPrincipal user,
        ISubscriptionBillingService subscriptionBillingService)
    {
        var username = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return Results.Unauthorized();
        }

        var response = new SubscribeResponse(request.CorrelationId());

        var subscription = await subscriptionBillingService.SubscribeAsync(username, username, request.PlanHandle);

        response.Subscription = new SubscriptionDto
        {
            SubscriptionId = subscription.SubscriptionId,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            PriceInCents = subscription.PriceInCents,
            Currency = subscription.Currency,
            State = subscription.State,
            ActivatedAt = subscription.ActivatedAt,
            NextBillingAt = subscription.NextBillingAt
        };

        return Results.Ok(response);
    }
}
