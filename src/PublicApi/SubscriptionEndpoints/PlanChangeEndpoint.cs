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
/// Preview and commit a plan change (UC3).
/// </summary>
/// <remarks>
/// The preview route computes the proration without applying anything. An immediate commit must echo the
/// previewed amount and timestamp back; the change is refused if the amount has moved since, so a caller
/// is never charged something other than what the preview showed.
/// </remarks>
public class PlanChangeEndpoint : IEndpoint<IResult, PlanChangeRequestDto, ClaimsPrincipal, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/plan-change/preview",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, PlanChangePreviewRequest request, ClaimsPrincipal user,
                ISubscriptionService subscriptionService) =>
            {
                var response = new PlanChangePreviewResponse(request.CorrelationId());
                var preview = await subscriptionService.PreviewPlanChangeAsync(
                    user.ToActor(), subscriptionId, request.TargetPlanHandle);
                response.Preview = preview.ToDto();
                return Results.Ok(response);
            })
            .Produces<PlanChangePreviewResponse>()
            .WithTags("SubscriptionEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId}/plan-change",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, PlanChangeRequestDto request, ClaimsPrincipal user,
                ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = subscriptionId;
                return await HandleAsync(request, user, subscriptionService);
            })
            .Produces<PlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        PlanChangeRequestDto request,
        ClaimsPrincipal user,
        ISubscriptionService subscriptionService)
    {
        var response = new PlanChangeResponse(request.CorrelationId());

        var subscription = await subscriptionService.ChangePlanAsync(
            user.ToActor(),
            request.SubscriptionId,
            new PlanChangeRequest
            {
                TargetPlanHandle = request.TargetPlanHandle,
                Timing = request.Timing,
                ConfirmedPaymentDueInCents = request.ConfirmedPaymentDueInCents,
                PreviewedAt = request.PreviewedAt
            });

        response.Subscription = subscription.ToDto();

        return Results.Ok(response);
    }
}
