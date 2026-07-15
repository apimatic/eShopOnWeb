using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>UC3 steps 3-6 — commit a previously previewed plan change.</summary>
public class CommitPlanChangeEndpoint : IEndpoint<IResult, CommitPlanChangeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/plan-change",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, CommitPlanChangeRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = subscriptionId;
                request.OwnerUserId = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS)
                    ? null
                    : user.Identity!.Name!;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<SubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CommitPlanChangeRequest request, ISubscriptionService subscriptionService)
    {
        var response = new SubscriptionResponse(request.CorrelationId());

        var confirmedPreview = SubscriptionMapping.FromDto(request.ConfirmedPreview);
        var subscription = await subscriptionService.CommitPlanChangeAsync(request.SubscriptionId, request.OwnerUserId, confirmedPreview);
        response.Subscription = SubscriptionMapping.ToDto(subscription);

        return Results.Ok(response);
    }
}
