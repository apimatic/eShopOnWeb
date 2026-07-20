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

/// <summary>Cancels a subscription (UC4), immediately or at the end of the current period. A caller acts on their own subscription; an Administrator may act on any.</summary>
public class CancelSubscriptionEndpoint : IEndpoint<IResult, CancelSubscriptionRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/cancel",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, CancelSubscriptionRequest request, ISubscriptionService subscriptionService, ClaimsPrincipal user) =>
            {
                Guard.Against.NullOrEmpty(user.Identity?.Name, nameof(user.Identity.Name));
                request.SubscriptionId = subscriptionId;
                request.OwnerReference = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS) ? null : user.Identity!.Name!;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<LifecycleActionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelSubscriptionRequest request, ISubscriptionService subscriptionService)
    {
        var response = new LifecycleActionResponse(request.CorrelationId());
        var updated = await subscriptionService.CancelAsync(request.SubscriptionId, request.EndOfPeriod, request.Reason, request.OwnerReference);
        response.Subscription = SubscriptionDto.FromModel(updated);
        return Results.Ok(response);
    }
}
