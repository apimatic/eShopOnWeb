using System.Security.Claims;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// UC3 step 4: commits a plan change. Requires the exact preview shown to the customer
/// (<see cref="PlanChangeRequest.ConfirmedPreview"/>) so a stale preview can never be silently applied.
/// </summary>
public class PlanChangeEndpoint : IEndpoint<IResult, PlanChangeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/plan-change",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlanChangeRequest request, ISubscriptionService subscriptionService, ClaimsPrincipal user) =>
            {
                request.CallerReference = user.Identity!.Name!;
                request.CallerIsAdmin = user.IsInRole(Constants.Roles.ADMINISTRATORS);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<PlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PlanChangeRequest request, ISubscriptionService subscriptionService)
    {
        var (subscription, denied) = await SubscriptionAccess.ResolveAsync(
            subscriptionService, request.CallerReference, request.CallerIsAdmin, request.SubscriptionId);
        if (denied != null)
        {
            return denied;
        }

        var response = new PlanChangeResponse(request.CorrelationId());
        var updated = await subscriptionService.CommitPlanChangeAsync(
            subscription!.Id, request.TargetProductHandle, request.ConfirmedPreview.ToDomain());
        response.Subscription = updated.ToDto();

        return Results.Ok(response);
    }
}
