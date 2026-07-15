using System;
using System.Security.Claims;
using System.Text.Json.Serialization;
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
/// Previews the effect of moving the caller's own subscription to a different plan (UC3,
/// step 2) — no charge is applied until PlanChangeEndpoint commits it.
/// </summary>
public class PlanChangePreviewEndpoint : IEndpoint<IResult, PlanChangeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/plan-change/preview",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlanChangeRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.UserId = user.Identity!.Name!;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<PlanChangePreviewResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PlanChangeRequest request, ISubscriptionService subscriptionService)
    {
        var response = new PlanChangePreviewResponse(request.CorrelationId());

        var preview = await subscriptionService.PreviewPlanChangeAsync(request.UserId, isAdmin: false, request.SubscriptionId, request.TargetProductHandle, request.ApplyNow);

        response.ProratedAdjustmentInCents = preview.ProratedAdjustmentInCents;
        response.ChargeInCents = preview.ChargeInCents;
        response.PaymentDueInCents = preview.PaymentDueInCents;
        response.CreditAppliedInCents = preview.CreditAppliedInCents;
        response.EffectiveAt = preview.EffectiveAt;

        return Results.Ok(response);
    }
}

/// <summary>
/// Commits a previously-previewed plan change on the caller's own subscription (UC3).
/// </summary>
public class PlanChangeEndpoint : IEndpoint<IResult, PlanChangeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/plan-change",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlanChangeRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.UserId = user.Identity!.Name!;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<PlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PlanChangeRequest request, ISubscriptionService subscriptionService)
    {
        var response = new PlanChangeResponse(request.CorrelationId());

        var subscription = await subscriptionService.CommitPlanChangeAsync(request.UserId, isAdmin: false, request.SubscriptionId, request.TargetProductHandle, request.ApplyNow);

        response.Subscription = SubscriptionDto.FromSubscription(subscription);

        return Results.Ok(response);
    }
}

public class PlanChangeRequest : BaseRequest
{
    public int SubscriptionId { get; set; }
    public string TargetProductHandle { get; set; } = string.Empty;
    public bool ApplyNow { get; set; }

    [JsonIgnore]
    public string UserId { get; set; } = string.Empty;
}

public class PlanChangePreviewResponse : BaseResponse
{
    public PlanChangePreviewResponse(Guid correlationId) : base(correlationId)
    {
    }

    public long ProratedAdjustmentInCents { get; set; }
    public long ChargeInCents { get; set; }
    public long PaymentDueInCents { get; set; }
    public long CreditAppliedInCents { get; set; }
    public DateTimeOffset EffectiveAt { get; set; }
}

public class PlanChangeResponse : BaseResponse
{
    public PlanChangeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionDto Subscription { get; set; } = new();
}
