using System;
using System.Security.Claims;
using System.Threading;
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
/// Preview and commit a plan change (UC3).
/// </summary>
/// <remarks>
/// Two routes on purpose: the preview quotes the cost and returns a fingerprint, and the commit
/// requires that fingerprint back. A quote that has moved in between is rejected rather than
/// silently charging a different amount.
/// </remarks>
public class PlanChangeEndpoint : IEndpoint<IResult, PlanChangeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/plan-change/preview",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int subscriptionId,
                PlanChangeRequest request,
                ClaimsPrincipal user,
                ISubscriptionService subscriptionService,
                CancellationToken cancellationToken) =>
            {
                request.SubscriptionId = subscriptionId;

                if (!await SubscriptionAuthorization.CanActOnSubscriptionAsync(
                        user, subscriptionId, subscriptionService, cancellationToken))
                {
                    return SubscriptionCaller.Forbidden();
                }

                if (string.IsNullOrWhiteSpace(request.TargetPlanHandle))
                {
                    return Results.BadRequest("targetPlanHandle is required.");
                }

                var preview = await subscriptionService.PreviewPlanChangeAsync(
                    subscriptionId, request.TargetPlanHandle, request.Timing, cancellationToken);

                return Results.Ok(new PlanChangePreviewResponse(request.CorrelationId())
                {
                    Preview = PlanChangePreviewDto.From(preview)
                });
            })
            .Produces<PlanChangePreviewResponse>()
            .WithTags("SubscriptionEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId}/plan-change",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int subscriptionId,
                PlanChangeRequest request,
                ClaimsPrincipal user,
                ISubscriptionService subscriptionService,
                CancellationToken cancellationToken) =>
            {
                request.SubscriptionId = subscriptionId;

                if (!await SubscriptionAuthorization.CanActOnSubscriptionAsync(
                        user, subscriptionId, subscriptionService, cancellationToken))
                {
                    return SubscriptionCaller.Forbidden();
                }

                return await HandleAsync(request, subscriptionService, cancellationToken);
            })
            .Produces<PlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(PlanChangeRequest request, ISubscriptionService subscriptionService)
        => HandleAsync(request, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        PlanChangeRequest request,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TargetPlanHandle))
        {
            return Results.BadRequest("targetPlanHandle is required.");
        }

        if (string.IsNullOrWhiteSpace(request.PreviewFingerprint))
        {
            return Results.BadRequest("previewFingerprint is required; preview the change first.");
        }

        var updated = await subscriptionService.ChangePlanAsync(
            request.SubscriptionId,
            request.TargetPlanHandle,
            request.Timing,
            request.PreviewFingerprint,
            cancellationToken);

        return Results.Ok(new PlanChangeResponse(request.CorrelationId())
        {
            Subscription = SubscriptionDto.From(updated)
        });
    }
}

public class PlanChangeRequest : BaseRequest
{
    /// <summary>Taken from the route; any value in the body is overwritten.</summary>
    public int SubscriptionId { get; set; }

    public string TargetPlanHandle { get; set; } = string.Empty;

    public PlanChangeTiming Timing { get; set; } = PlanChangeTiming.Immediate;

    /// <summary>
    /// The fingerprint returned by the preview. Required on commit; the change is refused when the
    /// quote has moved since it was shown.
    /// </summary>
    public string? PreviewFingerprint { get; set; }
}

public class PlanChangePreviewResponse : BaseResponse
{
    public PlanChangePreviewResponse(Guid correlationId) : base(correlationId)
    {
    }

    public PlanChangePreviewResponse()
    {
    }

    public PlanChangePreviewDto? Preview { get; set; }
}

public class PlanChangeResponse : BaseResponse
{
    public PlanChangeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public PlanChangeResponse()
    {
    }

    public SubscriptionDto? Subscription { get; set; }
}
