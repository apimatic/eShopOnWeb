using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>UC3 steps 1-2 — preview a plan change before the customer confirms it.</summary>
public class PreviewPlanChangeEndpoint : IEndpoint<IResult, PreviewPlanChangeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/plan-change/preview",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, PreviewPlanChangeRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = subscriptionId;
                request.OwnerUserId = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS)
                    ? null
                    : user.Identity!.Name!;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<PreviewPlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PreviewPlanChangeRequest request, ISubscriptionService subscriptionService)
    {
        var response = new PreviewPlanChangeResponse(request.CorrelationId());

        var timing = Enum.Parse<PlanChangeTiming>(request.Timing);
        var preview = await subscriptionService.PreviewPlanChangeAsync(request.SubscriptionId, request.OwnerUserId, request.TargetProductHandle, timing);
        response.Preview = SubscriptionMapping.ToDto(preview);

        return Results.Ok(response);
    }
}
