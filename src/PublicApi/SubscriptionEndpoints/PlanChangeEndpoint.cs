using System.Security.Claims;
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
/// Previews and commits a plan change (UC3). The preview route computes the prorated cost without
/// changing anything; the commit route applies it only if the previewed figures still hold.
/// </summary>
public class PlanChangeEndpoint : IEndpoint<IResult, PlanChangeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/plan-change/preview",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long subscriptionId, PlanChangeRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = subscriptionId;
                request.UserName = user.Identity?.Name;
                request.PreviewOnly = true;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<PlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId}/plan-change",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long subscriptionId, PlanChangeRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = subscriptionId;
                request.UserName = user.Identity?.Name;
                request.PreviewOnly = false;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<PlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PlanChangeRequest request, ISubscriptionService subscriptionService)
    {
        if (string.IsNullOrWhiteSpace(request.UserName))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.TargetPlanHandle))
        {
            return Results.BadRequest("targetPlanHandle is required.");
        }

        var response = new PlanChangeResponse(request.CorrelationId());

        if (request.PreviewOnly)
        {
            var preview = await subscriptionService.PreviewPlanChangeAsync(
                request.UserName, request.SubscriptionId, request.TargetPlanHandle, request.Timing);

            response.Preview = PlanChangePreviewDto.FromPreview(preview);
            return Results.Ok(response);
        }

        if (string.IsNullOrWhiteSpace(request.Fingerprint))
        {
            return Results.BadRequest("fingerprint from a preview is required to commit a plan change.");
        }

        var subscription = await subscriptionService.ChangePlanAsync(
            request.UserName, request.SubscriptionId, request.TargetPlanHandle, request.Timing, request.Fingerprint);

        response.Subscription = SubscriptionDto.FromSubscription(subscription);
        return Results.Ok(response);
    }
}
