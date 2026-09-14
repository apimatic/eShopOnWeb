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
/// Subscribes the authenticated caller to a Maxio plan. Ensures a Maxio customer exists for the
/// caller (idempotent - a double-click never creates two customers or two subscriptions).
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ISubscriptionBillingService billingService, ClaimsPrincipal user) =>
            {
                request.BuyerId = user.Identity?.Name ?? string.Empty;
                request.Email = request.BuyerId;
                return await HandleAsync(request, billingService);
            })
            .Produces<SubscribeResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionBillingService billingService)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("planHandle is required.");
        }

        if (string.IsNullOrWhiteSpace(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var response = new SubscribeResponse(request.CorrelationId());

        var subscription = await billingService.SubscribeAsync(request.BuyerId, request.Email, request.PlanHandle);

        response.Subscription = new CustomerSubscriptionDto
        {
            SubscriptionId = subscription.SubscriptionId,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            PriceInCents = subscription.PriceInCents,
            State = subscription.State,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextBillingAt = subscription.NextAssessmentAt,
            CreatedAt = subscription.CreatedAt
        };

        return Results.Ok(response);
    }
}
