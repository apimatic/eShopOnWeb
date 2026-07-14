using System;
using System.Security.Claims;
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

/// <summary>Previews a plan change with proration before it is committed (UC3 steps 1-2).</summary>
public class PlanChangePreviewEndpoint : IEndpoint<IResult, PlanChangeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/plan-change/preview",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, PlanChangeRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = subscriptionId;
                request.BuyerId = user.Identity!.Name!;
                request.IsAdmin = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<PlanChangePreviewResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PlanChangeRequest request, ISubscriptionService subscriptionService)
    {
        var response = new PlanChangePreviewResponse(request.CorrelationId());

        var timing = ParsePlanChangeTiming(request.Timing);
        var preview = await subscriptionService.PreviewPlanChangeAsync(
            request.SubscriptionId, request.BuyerId, request.IsAdmin, request.TargetProductHandle, timing);

        response.Preview = PlanChangePreviewDto.FromDomain(preview);

        return Results.Ok(response);
    }

    internal static PlanChangeTiming ParsePlanChangeTiming(string timing)
    {
        if (Enum.TryParse<PlanChangeTiming>(timing, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        throw new ArgumentException($"Unrecognized plan-change timing '{timing}'; expected 'Now' or 'AtRenewal'.", nameof(timing));
    }
}

/// <summary>Commits a previously previewed plan change (UC3 steps 3-5); rejects a stale preview.</summary>
public class PlanChangeCommitEndpoint : IEndpoint<IResult, PlanChangeCommitRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/plan-change/commit",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, PlanChangeCommitRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = subscriptionId;
                request.BuyerId = user.Identity!.Name!;
                request.IsAdmin = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<PlanChangeCommitResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PlanChangeCommitRequest request, ISubscriptionService subscriptionService)
    {
        var response = new PlanChangeCommitResponse(request.CorrelationId());

        var timing = PlanChangePreviewEndpoint.ParsePlanChangeTiming(request.Timing);
        var subscription = await subscriptionService.CommitPlanChangeAsync(
            request.SubscriptionId, request.BuyerId, request.IsAdmin, request.TargetProductHandle, timing, request.PreviewedAmountInCents);

        response.Subscription = SubscriptionDto.FromDomain(subscription);

        return Results.Ok(response);
    }
}
