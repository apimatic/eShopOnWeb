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
/// Previews what moving to another plan would cost, without changing anything (UC3 step 2).
/// </summary>
/// <remarks>
/// The returned fingerprint must be echoed back on the commit call so the customer can only be
/// charged the amount they were shown.
/// </remarks>
public class PlanChangePreviewEndpoint : IEndpoint<IResult, PlanChangePreviewRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/plan-change/preview",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, PlanChangePreviewRequest request, ClaimsPrincipal user,
             ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                request.Bind(subscriptionId, user.ResolveActingScope(), cancellationToken);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<PlanChangePreviewResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PlanChangePreviewRequest request, ISubscriptionService subscriptionService)
    {
        var response = new PlanChangePreviewResponse(request.CorrelationId());

        var preview = await subscriptionService.PreviewPlanChangeAsync(
            request.SubscriptionId,
            request.TargetPlanHandle,
            request.ResolveTiming(),
            request.ActingUserReference,
            request.CancellationToken);

        response.Preview = preview.ToDto();

        return Results.Ok(response);
    }
}
