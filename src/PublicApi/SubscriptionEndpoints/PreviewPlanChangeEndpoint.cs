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
/// [Authorize] Previews the cost of a plan change without committing it (UC3 step 2). The exact
/// preview returned must be echoed back on the commit call (<see cref="PlanChangeEndpoint"/>) so the
/// server can reject a stale commit.
/// </summary>
public class PreviewPlanChangeEndpoint : IEndpoint<IResult, PreviewPlanChangeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/plan-change/preview",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PreviewPlanChangeRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = await SubscriptionEndpointHelpers.ResolveSubscriptionIdAsync(subscriptionService, user, request.SubscriptionId);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<PreviewPlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PreviewPlanChangeRequest request, ISubscriptionService subscriptionService)
    {
        var response = new PreviewPlanChangeResponse(request.CorrelationId());
        var preview = await subscriptionService.PreviewPlanChangeAsync(request.SubscriptionId!.Value, request.TargetProductHandle, request.ApplyNow);
        response.Preview = ProrationPreviewDto.FromEntity(preview);
        return Results.Ok(response);
    }
}
