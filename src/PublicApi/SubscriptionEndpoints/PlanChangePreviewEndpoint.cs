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
/// Computes the prorated cost of a plan change without committing anything (UC3, step 2). The
/// returned <c>paymentDueInCents</c> is what the commit call must echo back.
/// </summary>
public class PlanChangePreviewEndpoint : IEndpoint<IResult, PlanChangePreviewRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/plan-change/preview",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId,
             PlanChangePreviewRequest request,
             ClaimsPrincipal user,
             ISubscriptionService subscriptionService,
             CancellationToken cancellationToken) =>
            {
                request.SubscriptionId = subscriptionId;
                request.UserReference = SubscriptionUser.ReferenceOf(user);
                return await HandleAsync(request, subscriptionService, cancellationToken);
            })
            .Produces<PlanChangePreviewResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(PlanChangePreviewRequest request, ISubscriptionService subscriptionService)
    {
        return HandleAsync(request, subscriptionService, CancellationToken.None);
    }

    public async Task<IResult> HandleAsync(PlanChangePreviewRequest request,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TargetPlanHandle))
        {
            return Results.BadRequest("A target plan handle is required.");
        }

        var timing = PlanChangeTimingParser.ParseTiming(request.Timing);

        var preview = await subscriptionService.PreviewPlanChangeAsync(request.UserReference,
            request.SubscriptionId,
            request.TargetPlanHandle,
            timing,
            cancellationToken);

        return Results.Ok(new PlanChangePreviewResponse(request.CorrelationId())
        {
            Preview = PlanChangePreviewDto.FromPreview(preview)
        });
    }
}
