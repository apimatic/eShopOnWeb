using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>UC3 step 2: preview the prorated cost (or at-renewal price) of a plan change before committing.</summary>
public class PreviewPlanChangeEndpoint : IEndpoint<IResult, PreviewPlanChangeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/plan-change/preview",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (PreviewPlanChangeRequest request, HttpContext httpContext, ISubscriptionService subscriptionService) =>
            {
                request.UserReference = httpContext.User.Identity!.Name!;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<PreviewPlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PreviewPlanChangeRequest request, ISubscriptionService subscriptionService)
    {
        var response = new PreviewPlanChangeResponse(request.CorrelationId());
        var preview = await subscriptionService.PreviewPlanChangeAsync(request.UserReference, request.SubscriptionId, request.TargetPlanHandle, request.ApplyNow);
        response.TargetPlanHandle = preview.TargetPlanHandle;
        response.Prorated = preview.Prorated;
        response.EffectiveDate = preview.EffectiveDate;
        response.ProratedAdjustmentInCents = preview.ProratedAdjustmentInCents;
        return Results.Ok(response);
    }
}

/// <summary>UC3 steps 3-4: commit a previously previewed plan change. Rejected as stale if the amount no longer matches a fresh preview.</summary>
public class CommitPlanChangeEndpoint : IEndpoint<IResult, CommitPlanChangeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/plan-change/commit",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CommitPlanChangeRequest request, HttpContext httpContext, ISubscriptionService subscriptionService) =>
            {
                request.UserReference = httpContext.User.Identity!.Name!;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<CommitPlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CommitPlanChangeRequest request, ISubscriptionService subscriptionService)
    {
        var response = new CommitPlanChangeResponse(request.CorrelationId());
        var subscription = await subscriptionService.CommitPlanChangeAsync(
            request.UserReference, request.SubscriptionId, request.TargetPlanHandle, request.ApplyNow, request.ExpectedProratedAdjustmentInCents);
        response.Subscription = SubscriptionDto.FromDomain(subscription);
        return Results.Ok(response);
    }
}
