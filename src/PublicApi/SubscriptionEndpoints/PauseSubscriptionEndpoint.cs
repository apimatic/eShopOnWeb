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

/// <summary>Pauses a subscription (UC4). A caller acts on their own subscription; an Administrator may act on any.</summary>
public class PauseSubscriptionEndpoint : IEndpoint<IResult, LifecycleActionRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/pause",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ISubscriptionService subscriptionService, ClaimsPrincipal user) =>
            {
                Guard.Against.NullOrEmpty(user.Identity?.Name, nameof(user.Identity.Name));
                var request = new LifecycleActionRequest
                {
                    SubscriptionId = subscriptionId,
                    OwnerReference = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS) ? null : user.Identity!.Name!
                };
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<LifecycleActionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(LifecycleActionRequest request, ISubscriptionService subscriptionService)
    {
        var response = new LifecycleActionResponse(request.CorrelationId());
        var updated = await subscriptionService.PauseAsync(request.SubscriptionId, request.OwnerReference);
        response.Subscription = SubscriptionDto.FromModel(updated);
        return Results.Ok(response);
    }
}
