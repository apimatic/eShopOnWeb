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
/// [Authorize] Commits a previewed plan change (UC3), either immediately (prorated) or at next
/// renewal (not prorated). Rejects with 409 Conflict if the freshly re-run preview no longer
/// matches <see cref="PlanChangeRequest.ExpectedPreview"/>.
/// </summary>
public class PlanChangeEndpoint : IEndpoint<IResult, PlanChangeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/plan-change",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlanChangeRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = await SubscriptionEndpointHelpers.ResolveSubscriptionIdAsync(subscriptionService, user, request.SubscriptionId);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<PlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PlanChangeRequest request, ISubscriptionService subscriptionService)
    {
        var response = new PlanChangeResponse(request.CorrelationId());
        var subscription = await subscriptionService.ChangePlanAsync(
            request.SubscriptionId!.Value,
            request.TargetProductHandle,
            request.ApplyNow,
            request.ExpectedPreview.ToEntity());
        response.Subscription = SubscriptionDto.FromEntity(subscription);
        return Results.Ok(response);
    }
}
