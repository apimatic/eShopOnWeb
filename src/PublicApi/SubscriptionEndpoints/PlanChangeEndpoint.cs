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
/// UC3 step 3-5: commits a previously previewed plan change. Rejects (via
/// <c>StalePlanChangePreviewException</c>) unless the caller presents the exact token the preview
/// endpoint issued and the subscription's plan has not drifted since — never applies a different
/// amount than the one the customer confirmed.
/// </summary>
public class PlanChangeEndpoint : IEndpoint<IResult, PlanChangeRequest, SubscriptionEndpointContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/plan-change/commit",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, PlanChangeRequest request, ClaimsPrincipal principal, ISubscriptionService subscriptionService) =>
            {
                var userReference = principal.Identity?.Name;
                if (string.IsNullOrEmpty(userReference)) return Results.Unauthorized();

                request.SubscriptionId = subscriptionId;
                var context = new SubscriptionEndpointContext(subscriptionService, userReference, principal.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS));
                return await HandleAsync(request, context);
            })
            .Produces<PlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PlanChangeRequest request, SubscriptionEndpointContext context)
    {
        var response = new PlanChangeResponse(request.CorrelationId());

        var subscription = await context.SubscriptionService.CommitPlanChangeAsync(
            request.SubscriptionId,
            context.UserReference,
            context.IsAdmin,
            request.PreviewToken);

        response.Subscription = subscription;
        return Results.Ok(response);
    }
}
