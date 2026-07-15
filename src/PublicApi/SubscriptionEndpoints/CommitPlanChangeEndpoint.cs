using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.Subscriptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Commits a previously previewed plan change, either now (with proration) or at the next
/// renewal (no proration) (UC3 steps 3-5).
/// </summary>
public class CommitPlanChangeEndpoint : IEndpoint<IResult, CommitPlanChangeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/plan-change",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, CommitPlanChangeRequest request, ISubscriptionService subscriptionService,
                HttpContext httpContext) =>
            {
                request.SubscriptionId = subscriptionId;
                request.OwnerReference = SubscriptionEndpointHelpers.ResolveOwnerReference(httpContext.User);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<CommitPlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CommitPlanChangeRequest request, ISubscriptionService subscriptionService)
    {
        var response = new CommitPlanChangeResponse(request.CorrelationId());

        var timing = string.Equals(request.Timing, nameof(PlanChangeTiming.AtNextRenewal),
            StringComparison.OrdinalIgnoreCase)
            ? PlanChangeTiming.AtNextRenewal
            : PlanChangeTiming.Now;

        var subscription = await subscriptionService.CommitPlanChangeAsync(request.OwnerReference,
            request.SubscriptionId, request.TargetPlanHandle, timing, request.ExpectedProratedAdjustmentInCents);

        response.Subscription = SubscriptionEndpointHelpers.ToDto(subscription);

        return Results.Ok(response);
    }
}
