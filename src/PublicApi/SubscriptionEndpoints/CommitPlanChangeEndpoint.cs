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

/// <summary>
/// Commits a plan change (UC3). The caller must pass back exactly the preview it was shown;
/// the service re-validates it against a fresh preview and rejects a stale one (never silently
/// applies a different amount than what was previewed).
/// </summary>
public class CommitPlanChangeEndpoint : IEndpoint<IResult, CommitPlanChangeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/plan-change",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, CommitPlanChangeRequest request, ISubscriptionService subscriptionService, ClaimsPrincipal user) =>
            {
                Guard.Against.NullOrEmpty(user.Identity?.Name, nameof(user.Identity.Name));
                request.SubscriptionId = subscriptionId;
                request.OwnerReference = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS) ? null : user.Identity!.Name!;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<CommitPlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CommitPlanChangeRequest request, ISubscriptionService subscriptionService)
    {
        var response = new CommitPlanChangeResponse(request.CorrelationId());

        var updated = await subscriptionService.CommitPlanChangeAsync(
            request.SubscriptionId,
            request.TargetProductHandle,
            request.ApplyNow,
            request.PreviouslyShownPreview.ToModel(),
            request.OwnerReference);

        response.Subscription = SubscriptionDto.FromModel(updated);
        return Results.Ok(response);
    }
}
