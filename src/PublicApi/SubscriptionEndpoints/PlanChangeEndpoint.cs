using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Commits a previously previewed plan change (UC3 step 4). The confirmed amounts are re-checked
/// against a fresh quote, so a stale preview is rejected rather than applied at a different price.
/// </summary>
public class PlanChangeEndpoint : IEndpoint<IResult, PlanChangeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscription-plan-changes",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlanChangeRequest request, ISubscriptionService subscriptionService) =>
                await HandleAsync(request, subscriptionService))
            .Produces<PlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PlanChangeRequest request, ISubscriptionService subscriptionService)
    {
        var response = new PlanChangeResponse(request.CorrelationId());

        var confirmed = new PlanChangePreview(request.TargetPlanHandle, request.Timing,
            request.ProratedAdjustmentInCents, request.ChargeInCents, request.PaymentDueInCents,
            request.CreditAppliedInCents);

        var subscription = await subscriptionService.ChangePlanAsync(request.SubscriptionId,
            request.TargetPlanHandle, request.Timing, confirmed);

        response.Subscription = subscription.ToDto();

        return Results.Ok(response);
    }
}
