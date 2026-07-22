using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>UC3 — computes a plan-change preview (prorated for "now", plan price for "at renewal").</summary>
public class PlanChangePreviewEndpoint : IEndpoint<IResult, PlanChangeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/plan-change/preview",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, PlanChangeRequest request, ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = subscriptionId;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<PlanChangePreviewResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PlanChangeRequest request, ISubscriptionService subscriptionService)
    {
        var preview = await subscriptionService.PreviewPlanChangeAsync(
            request.SubscriptionId, request.TargetProductHandle, request.ApplyImmediately);
        return Results.Ok(new PlanChangePreviewResponse(request.CorrelationId()) { Preview = preview.ToDto() });
    }
}

/// <summary>UC3 — commits the plan change, rejecting the commit if the confirmed preview is stale.</summary>
public class PlanChangeEndpoint : IEndpoint<IResult, PlanChangeCommitRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/plan-change",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, PlanChangeCommitRequest request, ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = subscriptionId;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<PlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PlanChangeCommitRequest request, ISubscriptionService subscriptionService)
    {
        var subscription = await subscriptionService.ChangePlanAsync(
            request.SubscriptionId, request.TargetProductHandle, request.ApplyImmediately,
            request.ConfirmedPreview.ToDomain());
        return Results.Ok(new PlanChangeResponse(request.CorrelationId()) { Subscription = subscription.ToDto() });
    }
}

public class PlanChangeRequest : BaseRequest
{
    public int SubscriptionId { get; set; }
    public string TargetProductHandle { get; set; } = string.Empty;
    public bool ApplyImmediately { get; set; } = true;
}

public class PlanChangeCommitRequest : BaseRequest
{
    public int SubscriptionId { get; set; }
    public string TargetProductHandle { get; set; } = string.Empty;
    public bool ApplyImmediately { get; set; } = true;
    public PlanChangePreviewDto ConfirmedPreview { get; set; } = new();
}

public class PlanChangePreviewResponse : BaseResponse
{
    public PlanChangePreviewResponse(Guid correlationId) : base(correlationId) { }

    public PlanChangePreviewResponse() { }

    public PlanChangePreviewDto Preview { get; set; } = new();
}

public class PlanChangeResponse : BaseResponse
{
    public PlanChangeResponse(Guid correlationId) : base(correlationId) { }

    public PlanChangeResponse() { }

    public CustomerSubscriptionDto Subscription { get; set; } = new();
}
