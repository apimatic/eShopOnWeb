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
/// UC3 step 2: previews the prorated charge/credit of moving to another plan, without committing.
/// </summary>
public class PlanChangePreviewEndpoint : IEndpoint<IResult, PlanChangePreviewRequest, SubscriptionEndpointContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/plan-change/preview",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, PlanChangePreviewRequest request, ISubscriptionService subscriptionService, ClaimsPrincipal user) =>
            {
                request.SubscriptionId = subscriptionId;
                return await HandleAsync(request, SubscriptionEndpointContext.From(subscriptionService, user));
            })
            .Produces<PlanChangePreviewResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PlanChangePreviewRequest request, SubscriptionEndpointContext context)
    {
        var response = new PlanChangePreviewResponse(request.CorrelationId());

        var preview = await context.SubscriptionService.PreviewPlanChangeAsync(
            context.UserId, request.SubscriptionId, request.TargetProductHandle, request.ApplyImmediately, context.IsAdmin);

        response.TargetProductHandle = preview.TargetProductHandle;
        response.ApplyImmediately = preview.ApplyImmediately;
        response.ProratedAdjustmentInCents = preview.ProratedAdjustmentInCents;
        response.ChargeInCents = preview.ChargeInCents;
        response.PaymentDueInCents = preview.PaymentDueInCents;
        response.CreditAppliedInCents = preview.CreditAppliedInCents;

        return Results.Ok(response);
    }
}
