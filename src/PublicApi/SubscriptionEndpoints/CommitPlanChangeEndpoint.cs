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
/// UC3 step 4: commits a previously previewed plan change. <see cref="CommitPlanChangeBody.CommitToken"/>
/// must be the token echoed back from the preview response - a stale preview (the pricing basis changed
/// since it was shown) is rejected rather than silently applying a different amount.
/// </summary>
public class CommitPlanChangeEndpoint : IEndpoint<IResult, CommitPlanChangeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/plan-change/commit",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, CommitPlanChangeBody body, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                var isAdmin = user.IsInRole(Constants.Roles.ADMINISTRATORS);
                var request = new CommitPlanChangeRequest(user.Identity!.Name!, isAdmin, subscriptionId, body.TargetProductHandle, body.Immediate, body.CommitToken);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<CommitPlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CommitPlanChangeRequest request, ISubscriptionService subscriptionService)
    {
        var response = new CommitPlanChangeResponse(request.CorrelationId());

        var subscription = await subscriptionService.CommitPlanChangeAsync(
            request.ActingBuyerId, request.IsAdmin, request.SubscriptionId, request.TargetProductHandle, request.Immediate, request.CommitToken);

        response.Subscription = SubscriptionDto.FromDomain(subscription);

        return Results.Ok(response);
    }
}
