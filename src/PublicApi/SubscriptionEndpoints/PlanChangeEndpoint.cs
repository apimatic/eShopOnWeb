using System;
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
/// UC3 step 4 — commits a plan change. Supply the fingerprint from the preview that was confirmed;
/// if the basis has moved the commit is refused with 409 rather than charging an unshown amount.
/// </summary>
public class PlanChangeEndpoint : IEndpoint<IResult, PlanChangeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/plan-change",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlanChangeRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.AuthenticatedUserName = SubscriptionEndpointResults.GetUserName(user);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<PlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PlanChangeRequest request, ISubscriptionService subscriptionService)
    {
        if (request.AuthenticatedUserName is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.TargetPlanHandle))
        {
            return Results.BadRequest(new { error = "targetPlanHandle is required." });
        }

        var response = new PlanChangeResponse(request.CorrelationId());

        try
        {
            var result = await subscriptionService.ChangePlanAsync(
                request.AuthenticatedUserName,
                request.SubscriptionId,
                request.TargetPlanHandle,
                request.Timing,
                request.Fingerprint);

            response.Subscription = SubscriptionDto.From(result.Subscription);
            response.PreviousPlanHandle = result.PreviousPlanHandle;
            response.NewPlanHandle = result.NewPlanHandle;
            response.ProrationAmount = result.ProrationAmount;
            response.EffectiveAt = result.EffectiveAt;
            response.AppliedPreview = PlanChangePreviewDto.From(result.AppliedPreview);
        }
        catch (Exception ex) when (SubscriptionEndpointResults.IsExpected(ex))
        {
            return SubscriptionEndpointResults.FromException(ex);
        }

        return Results.Ok(response);
    }
}
