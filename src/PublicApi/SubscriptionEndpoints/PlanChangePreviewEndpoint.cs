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

/// <summary>UC3 step 2: previews the cost of a plan change before the customer confirms it.</summary>
public class PlanChangePreviewEndpoint : IEndpoint<IResult, PlanChangePreviewRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/plan-change/preview",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlanChangePreviewRequest request, ISubscriptionService subscriptionService, ClaimsPrincipal user) =>
            {
                request.CallerReference = user.Identity!.Name!;
                request.CallerIsAdmin = user.IsInRole(Constants.Roles.ADMINISTRATORS);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<PlanChangePreviewResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PlanChangePreviewRequest request, ISubscriptionService subscriptionService)
    {
        var (subscription, denied) = await SubscriptionAccess.ResolveAsync(
            subscriptionService, request.CallerReference, request.CallerIsAdmin, request.SubscriptionId);
        if (denied != null)
        {
            return denied;
        }

        var response = new PlanChangePreviewResponse(request.CorrelationId());
        var preview = await subscriptionService.PreviewPlanChangeAsync(subscription!.Id, request.TargetProductHandle, request.ApplyImmediately);
        response.Preview = preview.ToDto();

        return Results.Ok(response);
    }
}
