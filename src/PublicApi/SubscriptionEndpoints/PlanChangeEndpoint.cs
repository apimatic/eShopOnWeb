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
/// Commits a previously previewed plan change (UC3, step 4).
/// <para>
/// The preview signature the customer confirmed must be supplied. The change is re-priced and
/// refused as stale if it no longer matches, so the amount charged is always the amount that was
/// shown.
/// </para>
/// </summary>
public class PlanChangeEndpoint : IEndpoint<IResult, PlanChangeRequest, ISubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PlanChangeEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

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
        var userReference = _httpContextAccessor.CurrentUserReference();
        if (userReference is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.TargetPlanHandle))
        {
            return Results.BadRequest("A target plan handle is required.");
        }

        if (string.IsNullOrWhiteSpace(request.PreviewSignature))
        {
            return Results.BadRequest("A preview signature is required. Request a preview and confirm it before committing.");
        }

        if (!PlanChangePreviewEndpoint.TryParseTiming(request.Timing, out var timing))
        {
            return Results.BadRequest("Timing must be 'Immediate' or 'AtNextRenewal'.");
        }

        var subscription = await subscriptionService.ChangePlanAsync(userReference,
            request.SubscriptionId,
            request.TargetPlanHandle,
            timing,
            request.PreviewSignature);

        return Results.Ok(new PlanChangeResponse(request.CorrelationId())
        {
            Subscription = subscription.ToDto()
        });
    }
}
