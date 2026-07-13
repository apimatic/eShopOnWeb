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
/// Previews the prorated cost of moving a subscription to another plan, before it's committed (UC3).
/// </summary>
public class PreviewPlanChangeEndpoint : IEndpoint<IResult, PreviewPlanChangeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/plan-change/preview",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, PreviewPlanChangeRequest body, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                var request = new PreviewPlanChangeRequest(subscriptionId, body.TargetProductHandle, user.Identity!.Name!);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<PreviewPlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PreviewPlanChangeRequest request, ISubscriptionService subscriptionService)
    {
        var response = new PreviewPlanChangeResponse(request.CorrelationId());

        var preview = await subscriptionService.PreviewPlanChangeAsync(request.UserReference, request.SubscriptionId, request.TargetProductHandle);

        response.ProratedAdjustmentInCents = preview.ProratedAdjustmentInCents;
        response.ChargeInCents = preview.ChargeInCents;
        response.PaymentDueInCents = preview.PaymentDueInCents;
        response.CreditAppliedInCents = preview.CreditAppliedInCents;

        return Results.Ok(response);
    }
}
