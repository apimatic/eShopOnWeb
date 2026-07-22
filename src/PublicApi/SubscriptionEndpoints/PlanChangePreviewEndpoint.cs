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
/// Quote the prorated cost of a plan change without committing it (UC3, step 2).
/// </summary>
public class PlanChangePreviewEndpoint : IEndpoint<IResult, PlanChangePreviewRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/plan-change/preview",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, HttpRequest httpRequest, ClaimsPrincipal user,
                ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                var request = PlanChangePreviewRequest.From(await SubscriptionRequestBody.ReadAsync(httpRequest, cancellationToken));
                return await HandleAsync(subscriptionId, request, user, subscriptionService, cancellationToken);
            })
            .Accepts<PlanChangePreviewRequest>("application/json")
            .Produces<PlanChangePreviewResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(PlanChangePreviewRequest request, ISubscriptionService subscriptionService)
        => HandleAsync(0, request, new ClaimsPrincipal(), subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(int subscriptionId, PlanChangePreviewRequest request, ClaimsPrincipal user,
        ISubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        var timing = request.ResolveTiming();

        var preview = await subscriptionService.PreviewPlanChangeAsync(user.ToSubscriptionActor(), subscriptionId,
            request.PlanHandle, timing, cancellationToken);

        return Results.Ok(PlanChangePreviewResponse.From(request.CorrelationId(), preview.ToDto()));
    }
}
