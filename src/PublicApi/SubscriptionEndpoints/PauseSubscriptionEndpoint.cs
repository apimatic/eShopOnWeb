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

/// <summary>UC4 — pause an active subscription.</summary>
public class PauseSubscriptionEndpoint : IEndpoint<IResult, PauseSubscriptionRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/pause",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                var request = new PauseSubscriptionRequest
                {
                    SubscriptionId = subscriptionId,
                    OwnerUserId = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS) ? null : user.Identity!.Name!
                };
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<SubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PauseSubscriptionRequest request, ISubscriptionService subscriptionService)
    {
        var response = new SubscriptionResponse(request.CorrelationId());
        var subscription = await subscriptionService.PauseAsync(request.SubscriptionId, request.OwnerUserId);
        response.Subscription = SubscriptionMapping.ToDto(subscription);
        return Results.Ok(response);
    }
}
