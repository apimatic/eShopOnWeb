using System.Security.Claims;
using System.Threading;
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
/// Preview and commit a plan change (UC3).
/// </summary>
/// <remarks>
/// The two routes are deliberately separate: the preview quotes the change without applying it,
/// and the commit refuses unless the caller echoes back the token of the quote they were shown, so
/// a price that moved in between can never be charged silently.
/// </remarks>
public class PlanChangeEndpoint : IEndpoint<IResult, PlanChangeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/plan-change/preview",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, PlanChangeRequest request, ClaimsPrincipal user,
             ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                Bind(request, subscriptionId, user, cancellationToken);
                return await PreviewAsync(request, subscriptionService);
            })
            .Produces<PlanChangePreviewResponse>()
            .WithTags("SubscriptionEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId:int}/plan-change",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, PlanChangeRequest request, ClaimsPrincipal user,
             ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                Bind(request, subscriptionId, user, cancellationToken);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<PlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    /// <summary>Commits the plan change.</summary>
    public Task<IResult> HandleAsync(PlanChangeRequest request, ISubscriptionService subscriptionService) =>
        SubscriptionEndpointSupport.ExecuteAsync(async () =>
        {
            var denied = await GuardAsync(request, subscriptionService);
            if (denied is not null)
            {
                return denied;
            }

            if (string.IsNullOrWhiteSpace(request.PreviewToken))
            {
                return Results.BadRequest(new
                {
                    error = "previewToken is required. Take a preview first and echo its token back to confirm."
                });
            }

            var timing = SubscriptionEndpointSupport.ParseTiming(request.Timing);
            var response = new PlanChangeResponse(request.CorrelationId());

            var subscription = await subscriptionService.ChangePlanAsync(
                request.SubscriptionId, request.TargetPlanHandle, timing, request.PreviewToken, request.CancellationToken);

            response.Subscription = SubscriptionEndpointSupport.ToDto(subscription);

            return Results.Ok(response);
        });

    /// <summary>Quotes the plan change without applying it.</summary>
    public Task<IResult> PreviewAsync(PlanChangeRequest request, ISubscriptionService subscriptionService) =>
        SubscriptionEndpointSupport.ExecuteAsync(async () =>
        {
            var denied = await GuardAsync(request, subscriptionService);
            if (denied is not null)
            {
                return denied;
            }

            var timing = SubscriptionEndpointSupport.ParseTiming(request.Timing);
            var response = new PlanChangePreviewResponse(request.CorrelationId());

            var preview = await subscriptionService.PreviewPlanChangeAsync(
                request.SubscriptionId, request.TargetPlanHandle, timing, request.CancellationToken);

            response.Preview = SubscriptionEndpointSupport.ToDto(preview);

            return Results.Ok(response);
        });

    private static void Bind(PlanChangeRequest request, int subscriptionId, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        request.SubscriptionId = subscriptionId;
        request.User = user;
        request.CancellationToken = cancellationToken;
    }

    private static async Task<IResult?> GuardAsync(PlanChangeRequest request, ISubscriptionService subscriptionService)
    {
        if (string.IsNullOrWhiteSpace(request.TargetPlanHandle))
        {
            return Results.BadRequest(new { error = "targetPlanHandle is required." });
        }

        return await SubscriptionEndpointSupport.EnsureCallerMayActOnAsync(
            request.User, request.SubscriptionId, subscriptionService, request.CancellationToken);
    }
}
