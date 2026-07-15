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
/// Previews the prorated cost of moving a subscription to a different plan (UC3 step 1-2).
/// </summary>
public class PreviewPlanChangeEndpoint : IEndpoint<IResult, PreviewPlanChangeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/plan-change/preview",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, PreviewPlanChangeRequest request, ISubscriptionService subscriptionService,
                HttpContext httpContext) =>
            {
                request.SubscriptionId = subscriptionId;
                request.OwnerReference = SubscriptionEndpointHelpers.ResolveOwnerReference(httpContext.User);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<PreviewPlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PreviewPlanChangeRequest request, ISubscriptionService subscriptionService)
    {
        var response = new PreviewPlanChangeResponse(request.CorrelationId());

        var preview = await subscriptionService.PreviewPlanChangeAsync(request.OwnerReference,
            request.SubscriptionId, request.TargetPlanHandle);

        response.Preview = new PlanChangePreviewDto
        {
            TargetPlanHandle = preview.TargetPlanHandle,
            ProratedAdjustmentInCents = preview.ProratedAdjustmentInCents,
            ChargeInCents = preview.ChargeInCents,
            PaymentDueInCents = preview.PaymentDueInCents,
            CreditAppliedInCents = preview.CreditAppliedInCents,
        };

        return Results.Ok(response);
    }
}
