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
/// Commits an upgrade or downgrade with the chosen proration timing (UC3)
/// </summary>
public class PlanChangeEndpoint : IEndpoint<IResult, PlanChangeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/plan-change",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, PlanChangeRequest request, ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = subscriptionId;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<PlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PlanChangeRequest request, ISubscriptionService subscriptionService)
    {
        var response = new PlanChangeResponse(request.CorrelationId());

        var result = await subscriptionService.ChangePlanAsync(
            request.SubscriptionId, request.TargetPlanHandle, request.Timing, request.ConfirmedPaymentDue);

        response.OldPlanHandle = result.OldPlanHandle;
        response.NewPlanHandle = result.NewPlanHandle;
        response.Timing = result.Timing;
        response.EffectiveAt = result.EffectiveAt;
        response.Subscription = result.Subscription.ToDto();

        return Results.Ok(response);
    }
}
