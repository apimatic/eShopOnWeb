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
/// Quotes the prorated cost of an upgrade or downgrade before it is committed (UC3)
/// </summary>
public class PlanChangePreviewEndpoint : IEndpoint<IResult, PlanChangePreviewRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/plan-change/preview",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, PlanChangePreviewRequest request, ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = subscriptionId;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<PlanChangePreviewResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PlanChangePreviewRequest request, ISubscriptionService subscriptionService)
    {
        var response = new PlanChangePreviewResponse(request.CorrelationId());

        var preview = await subscriptionService.PreviewPlanChangeAsync(
            request.SubscriptionId, request.TargetPlanHandle, request.Timing);

        response.SubscriptionId = preview.SubscriptionId;
        response.CurrentPlanHandle = preview.CurrentProductHandle;
        response.TargetPlanHandle = preview.TargetProductHandle;
        response.Prorate = preview.Prorate;
        response.ProratedAdjustment = preview.ProratedAdjustment;
        response.Charge = preview.Charge;
        response.PaymentDue = preview.PaymentDue;
        response.CreditApplied = preview.CreditApplied;

        return Results.Ok(response);
    }
}
