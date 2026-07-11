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

/// <summary>UC3 step 2: previews the prorated cost/credit of a plan change without applying it.</summary>
public class PreviewPlanChangeEndpoint : IEndpoint<IResult, PreviewPlanChangeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/plan-change/preview",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, PreviewPlanChangeBody body, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                var isAdmin = user.IsInRole(Constants.Roles.ADMINISTRATORS);
                var request = new PreviewPlanChangeRequest(user.Identity!.Name!, isAdmin, subscriptionId, body.TargetProductHandle, body.Immediate);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<PreviewPlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PreviewPlanChangeRequest request, ISubscriptionService subscriptionService)
    {
        var response = new PreviewPlanChangeResponse(request.CorrelationId());

        var preview = await subscriptionService.PreviewPlanChangeAsync(
            request.ActingBuyerId, request.IsAdmin, request.SubscriptionId, request.TargetProductHandle, request.Immediate);

        response.Preview = PlanChangePreviewDto.FromDomain(preview);

        return Results.Ok(response);
    }
}
