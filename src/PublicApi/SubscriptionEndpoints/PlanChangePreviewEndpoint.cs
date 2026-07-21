using System.Security.Claims;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>UC3 step 1-2 — previews the prorated cost of a plan change before it is committed.</summary>
public class PlanChangePreviewEndpoint : IEndpoint<IResult, PlanChangePreviewRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/plan-change/preview",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlanChangePreviewRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.UserName = user.Identity?.Name ?? string.Empty;
                request.IsAdministrator = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<PlanChangePreviewResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PlanChangePreviewRequest request, ISubscriptionService subscriptionService)
    {
        Guard.Against.NullOrEmpty(request.UserName, nameof(request.UserName));

        if (!await SubscriptionAccessControl.CanAccessAsync(subscriptionService, request.UserName, request.IsAdministrator, request.SubscriptionId))
        {
            return Results.Forbid();
        }

        var preview = await subscriptionService.PreviewPlanChangeAsync(request.SubscriptionId, request.TargetPlanHandle, request.ApplyNow);

        var response = new PlanChangePreviewResponse(request.CorrelationId())
        {
            FromPlanHandle = preview.FromPlanHandle,
            ToPlanHandle = preview.ToPlanHandle,
            ApplyNow = preview.ApplyNow,
            ProratedAmount = preview.ProratedAmount,
            PaymentDueAmount = preview.PaymentDueAmount,
            CreditAppliedAmount = preview.CreditAppliedAmount,
            EffectiveDate = preview.EffectiveDate
        };

        return Results.Ok(response);
    }
}
