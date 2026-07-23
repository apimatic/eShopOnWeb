using System.Threading;
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
/// Preview what moving a subscription to another plan would cost
/// </summary>
public class PlanChangePreviewEndpoint : IEndpoint<IResult, PlanChangePreviewRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/plan-change-preview",
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

        var preview = await subscriptionService.PreviewPlanChangeAsync(request.SubscriptionId, request.TargetPlanHandle,
            CancellationToken.None);

        response.TargetPlanHandle = preview.TargetPlanHandle;
        response.ProratedAdjustment = preview.ProratedAdjustment;
        response.Charge = preview.Charge;
        response.PaymentDue = preview.PaymentDue;
        response.CreditApplied = preview.CreditApplied;

        return Results.Ok(response);
    }
}
