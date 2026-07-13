using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// UC3 step 4: commits a previously-previewed plan change.
/// <see cref="CommitPlanChangeBody.ExpectedProratedAdjustmentInCents"/> must match a freshly
/// recomputed preview or the commit is rejected (stale-preview protection).
/// </summary>
public class CommitPlanChangeEndpoint : IEndpoint<IResult, CommitPlanChangeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/plan-change/commit",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long subscriptionId, CommitPlanChangeBody body, HttpContext httpContext, ISubscriptionService subscriptionService) =>
            {
                var customerReference = httpContext.User.Identity?.Name ?? string.Empty;
                var actingAsAdmin = httpContext.User.IsInRole(Constants.Roles.ADMINISTRATORS);
                var request = new CommitPlanChangeRequest(subscriptionId, customerReference, actingAsAdmin, body);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<CommitPlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CommitPlanChangeRequest request, ISubscriptionService subscriptionService)
    {
        var response = new CommitPlanChangeResponse(request.CorrelationId());

        var subscription = await subscriptionService.CommitPlanChangeAsync(
            request.CustomerReference, request.ActingAsAdmin, request.SubscriptionId, request.TargetProductHandle,
            request.Timing, request.ExpectedProratedAdjustmentInCents);

        response.Subscription = subscription.ToDto();
        return Results.Ok(response);
    }
}
