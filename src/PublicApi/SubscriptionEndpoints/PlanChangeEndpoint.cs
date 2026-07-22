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
/// Previews and commits a plan change with proration (UC3)
/// </summary>
/// <remarks>
/// Preview first, then commit with the fingerprint the preview returned. A commit whose fingerprint
/// no longer matches the provider's current numbers is refused, so the customer is never charged an
/// amount other than the one they confirmed.
/// </remarks>
public class PlanChangeEndpoint : IEndpoint<IResult, PlanChangeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/plan-change/preview",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlanChangeRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService,
                CancellationToken cancellationToken) =>
            {
                request.UserReference = user.Identity?.Name ?? string.Empty;
                return await PreviewAsync(request, subscriptionService, cancellationToken);
            })
            .Produces<PlanChangePreviewResponse>()
            .WithTags("SubscriptionEndpoints");

        app.MapPost("api/subscriptions/plan-change",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlanChangeRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService,
                CancellationToken cancellationToken) =>
            {
                request.UserReference = user.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, subscriptionService, cancellationToken);
            })
            .Produces<PlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(PlanChangeRequest request, ISubscriptionService subscriptionService) =>
        HandleAsync(request, subscriptionService, CancellationToken.None);

    public async Task<IResult> PreviewAsync(PlanChangeRequest request,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        if (!TryValidate(request, out var timing, out var failure))
        {
            return failure;
        }

        var response = new PlanChangePreviewResponse(request.CorrelationId());

        var preview = await subscriptionService.PreviewPlanChangeAsync(request.UserReference,
            request.TargetPlanHandle, timing, cancellationToken);
        response.Preview = preview.ToDto();

        return Results.Ok(response);
    }

    public async Task<IResult> HandleAsync(PlanChangeRequest request,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        if (!TryValidate(request, out var timing, out var failure))
        {
            return failure;
        }

        if (string.IsNullOrWhiteSpace(request.PreviewFingerprint))
        {
            return Results.BadRequest(
                "A preview fingerprint is required. Call the preview route first and echo its fingerprint back.");
        }

        var response = new PlanChangeResponse(request.CorrelationId());

        var subscription = await subscriptionService.ChangePlanAsync(request.UserReference,
            request.TargetPlanHandle, timing, request.PreviewFingerprint, cancellationToken);
        response.Subscription = subscription.ToDto();

        return Results.Ok(response);
    }

    private static bool TryValidate(PlanChangeRequest request, out PlanChangeTiming timing, out IResult failure)
    {
        timing = PlanChangeTiming.Immediately;

        if (string.IsNullOrWhiteSpace(request.UserReference))
        {
            failure = Results.Unauthorized();
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.TargetPlanHandle))
        {
            failure = Results.BadRequest("A target plan handle is required.");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(request.Timing) &&
            !Enum.TryParse(request.Timing, true, out timing))
        {
            failure = Results.BadRequest(
                $"Timing must be '{nameof(PlanChangeTiming.Immediately)}' or '{nameof(PlanChangeTiming.AtNextRenewal)}'.");
            return false;
        }

        failure = Results.Empty;
        return true;
    }
}
