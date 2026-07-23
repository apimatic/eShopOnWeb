using System;
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
/// Previews and commits a plan change (plan.md UC3). The preview charges nothing; the commit echoes back
/// the previewed net amount and is rejected if the proration basis has moved since.
/// </summary>
public class PlanChangeEndpoint : IEndpoint<IResult, PlanChangeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/plan-change/preview",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, PlanChangeRequest request, HttpContext http,
                ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                request.SetContext(subscriptionId, SubscriptionCaller.Restriction(http.User), previewOnly: true);
                return await HandleAsync(request, subscriptionService, cancellationToken);
            })
            .Produces<PlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId}/plan-change",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, PlanChangeRequest request, HttpContext http,
                ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                request.SetContext(subscriptionId, SubscriptionCaller.Restriction(http.User), previewOnly: false);
                return await HandleAsync(request, subscriptionService, cancellationToken);
            })
            .Produces<PlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(PlanChangeRequest request, ISubscriptionService subscriptionService) =>
        HandleAsync(request, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(PlanChangeRequest request, ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TargetPlanHandle))
        {
            return Results.BadRequest("targetPlanHandle is required.");
        }

        if (!TryParseTiming(request.Timing, out var timing))
        {
            return Results.BadRequest(
                $"timing must be '{PlanChangeTiming.Immediately}' or '{PlanChangeTiming.AtNextRenewal}'.");
        }

        var response = new PlanChangeResponse(request.CorrelationId());

        if (request.PreviewOnly)
        {
            var preview = await subscriptionService.PreviewPlanChangeAsync(
                request.SubscriptionId, request.TargetPlanHandle, timing, request.RestrictToUserReference,
                cancellationToken);

            response.Preview = PlanChangePreviewDto.From(preview);
            return Results.Ok(response);
        }

        if (request.PreviewedNetAmount is null)
        {
            return Results.BadRequest(
                "previewedNetAmount is required; call the preview endpoint first and echo back its netAmount.");
        }

        var updated = await subscriptionService.ChangePlanAsync(
            request.SubscriptionId, request.TargetPlanHandle, timing, request.PreviewedNetAmount.Value,
            request.RestrictToUserReference, cancellationToken);

        response.Subscription = SubscriptionDto.From(updated);
        return Results.Ok(response);
    }

    private static bool TryParseTiming(string? value, out PlanChangeTiming timing)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            timing = PlanChangeTiming.Immediately;
            return true;
        }

        return Enum.TryParse(value, ignoreCase: true, out timing) && Enum.IsDefined(timing);
    }
}
