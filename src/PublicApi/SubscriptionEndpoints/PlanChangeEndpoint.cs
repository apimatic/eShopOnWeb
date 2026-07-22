using System.Security.Claims;
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
/// Commits a previously previewed plan change (UC3 step 4).
/// </summary>
/// <remarks>
/// Requires the fingerprint from the preview. If the cost has moved since, the change is rejected
/// with 409 and a fresh preview is required.
/// </remarks>
public class PlanChangeEndpoint : IEndpoint<IResult, PlanChangeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/plan-change",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, PlanChangeRequest request, ClaimsPrincipal user,
             ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                request.Bind(subscriptionId, user.ResolveActingScope(), cancellationToken);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<PlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PlanChangeRequest request, ISubscriptionService subscriptionService)
    {
        var response = new PlanChangeResponse(request.CorrelationId());

        var subscription = await subscriptionService.ChangePlanAsync(
            request.SubscriptionId,
            request.TargetPlanHandle,
            request.ResolveTiming(),
            request.ConfirmedFingerprint,
            request.ActingUserReference,
            request.CancellationToken);

        response.Subscription = subscription.ToDto();

        return Results.Ok(response);
    }
}
