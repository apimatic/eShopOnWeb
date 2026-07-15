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

/// <summary>UC4 — cancel a subscription, immediately or at the end of the current period.</summary>
public class CancelSubscriptionEndpoint : IEndpoint<IResult, CancelSubscriptionRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/cancel",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, CancelSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
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

    public async Task<IResult> HandleAsync(CancelSubscriptionRequest request, ISubscriptionService subscriptionService)
    {
        var response = new SubscriptionResponse(request.CorrelationId());

        var timing = Enum.Parse<CancellationTiming>(request.Timing);
        var subscription = await subscriptionService.CancelAsync(request.SubscriptionId, request.OwnerUserId, timing, request.Reason);
        response.Subscription = SubscriptionMapping.ToDto(subscription);

        return Results.Ok(response);
    }
}
