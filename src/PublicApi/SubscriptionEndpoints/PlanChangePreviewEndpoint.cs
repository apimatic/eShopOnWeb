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
/// Preview the prorated cost of moving a subscription to another plan (UC3 step 2)
/// </summary>
public class PlanChangePreviewEndpoint : IEndpoint<IResult, PlanChangeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/plan-change/preview",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, PlanChangeRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                request.SubscriptionId = subscriptionId;

                return await HandleAsync(request, user.OwnershipScope(), subscriptionService, cancellationToken);
            })
            .Produces<PlanChangePreviewResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(PlanChangeRequest request, ISubscriptionService subscriptionService) =>
        HandleAsync(request, null, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(PlanChangeRequest request, string? ownershipScope, ISubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        var preview = await subscriptionService.PreviewPlanChangeAsync(
            ownershipScope, request.SubscriptionId, request.TargetPlanHandle, request.ApplyImmediately, cancellationToken);

        var response = new PlanChangePreviewResponse(request.CorrelationId())
        {
            Preview = PlanChangePreviewDto.From(preview)
        };

        return Results.Ok(response);
    }
}
