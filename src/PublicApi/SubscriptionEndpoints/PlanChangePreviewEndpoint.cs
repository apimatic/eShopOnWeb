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
/// Quotes the prorated cost of moving to another plan without committing it (UC3, step 2).
/// </summary>
public class PlanChangePreviewEndpoint : SubscriptionEndpointBase,
    IEndpoint<IResult, PlanChangePreviewRequest, ISubscriptionService>
{
    public PlanChangePreviewEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor)
    {
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/plan-change/preview",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlanChangePreviewRequest request, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<PlanChangePreviewResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PlanChangePreviewRequest request,
        ISubscriptionService subscriptionService)
    {
        var userReference = ResolveUserReference(request.UserReference);
        if (userReference is null)
        {
            return Denied();
        }

        if (string.IsNullOrWhiteSpace(request.TargetPlanHandle))
        {
            return Results.BadRequest("A target plan handle is required.");
        }

        var response = new PlanChangePreviewResponse(request.CorrelationId());

        var preview = await subscriptionService.PreviewPlanChangeAsync(userReference, request.TargetPlanHandle);

        response.TargetPlanHandle = preview.TargetPlanHandle;
        response.ProratedAdjustment = preview.ProratedAdjustment;
        response.Charge = preview.Charge;
        response.PaymentDue = preview.PaymentDue;
        response.CreditApplied = preview.CreditApplied;

        return Results.Ok(response);
    }
}
