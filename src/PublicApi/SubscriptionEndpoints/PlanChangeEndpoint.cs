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
/// UC3 step 4: commits a plan change. The caller must echo back the exact amounts returned by
/// <see cref="PlanChangePreviewEndpoint"/> - the service re-previews and rejects the commit if they no
/// longer match (never silently applies a different amount than the one shown, §UC3).
/// </summary>
public class PlanChangeEndpoint : IEndpoint<IResult, PlanChangeRequest, SubscriptionEndpointContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/plan-change",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, PlanChangeRequest request, ISubscriptionService subscriptionService, ClaimsPrincipal user) =>
            {
                request.SubscriptionId = subscriptionId;
                return await HandleAsync(request, SubscriptionEndpointContext.From(subscriptionService, user));
            })
            .Produces<PlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PlanChangeRequest request, SubscriptionEndpointContext context)
    {
        var response = new PlanChangeResponse(request.CorrelationId());

        var expectedPreview = new PlanChangePreview(
            request.TargetProductHandle,
            request.ApplyImmediately,
            request.ProratedAdjustmentInCents,
            request.ChargeInCents,
            request.PaymentDueInCents,
            request.CreditAppliedInCents);

        var updated = await context.SubscriptionService.CommitPlanChangeAsync(
            context.UserId, request.SubscriptionId, request.TargetProductHandle, request.ApplyImmediately, expectedPreview, context.IsAdmin);

        response.Subscription = SubscriptionMapping.ToDto(updated);

        return Results.Ok(response);
    }
}
