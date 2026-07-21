using System.Security.Claims;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// UC3 step 3-4 — commits a previously previewed plan change. Rejects (409, via
/// <see cref="ApplicationCore.Exceptions.PlanChangePreviewStaleException"/>) if the proration
/// basis has changed since the preview was shown.
/// </summary>
public class PlanChangeCommitEndpoint : IEndpoint<IResult, PlanChangeCommitRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/plan-change/commit",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlanChangeCommitRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.UserName = user.Identity?.Name ?? string.Empty;
                request.IsAdministrator = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<PlanChangeCommitResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PlanChangeCommitRequest request, ISubscriptionService subscriptionService)
    {
        Guard.Against.NullOrEmpty(request.UserName, nameof(request.UserName));

        if (!await SubscriptionAccessControl.CanAccessAsync(subscriptionService, request.UserName, request.IsAdministrator, request.SubscriptionId))
        {
            return Results.Forbid();
        }

        var subscription = await subscriptionService.CommitPlanChangeAsync(
            request.SubscriptionId, request.TargetPlanHandle, request.ApplyNow, request.ExpectedProratedAmount);

        var response = new PlanChangeCommitResponse(request.CorrelationId())
        {
            Subscription = SubscriptionDto.FromDomain(subscription)
        };

        return Results.Ok(response);
    }
}
