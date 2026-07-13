using System.Security.Claims;
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
/// UC3 step 1-2: previews the prorated cost (or, for an at-renewal change, the new plan price) of
/// moving a subscription to a different plan, and issues a short-lived token the commit endpoint must
/// present unchanged (§6 Phase 4 — never silently apply a different amount than the one previewed).
/// </summary>
public class PlanChangePreviewEndpoint : IEndpoint<IResult, PlanChangePreviewRequest, SubscriptionEndpointContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/plan-change/preview",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, PlanChangePreviewRequest request, ClaimsPrincipal principal, ISubscriptionService subscriptionService) =>
            {
                var userReference = principal.Identity?.Name;
                if (string.IsNullOrEmpty(userReference)) return Results.Unauthorized();

                request.SubscriptionId = subscriptionId;
                var context = new SubscriptionEndpointContext(subscriptionService, userReference, principal.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS));
                return await HandleAsync(request, context);
            })
            .Produces<PlanChangePreviewResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PlanChangePreviewRequest request, SubscriptionEndpointContext context)
    {
        var response = new PlanChangePreviewResponse(request.CorrelationId());

        var preview = await context.SubscriptionService.PreviewPlanChangeAsync(
            request.SubscriptionId,
            context.UserReference,
            context.IsAdmin,
            request.TargetProductHandle,
            request.ApplyAtRenewal);

        response.Preview = preview;
        return Results.Ok(response);
    }
}
