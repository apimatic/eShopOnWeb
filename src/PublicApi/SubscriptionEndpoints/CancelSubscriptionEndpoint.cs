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
/// Cancels a subscription, either immediately or at the end of the current billing period (UC4).
/// </summary>
public class CancelSubscriptionEndpoint : IEndpoint<IResult, CancelSubscriptionRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/cancel",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, CancelSubscriptionRequest body, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                var request = new CancelSubscriptionRequest(subscriptionId, body.EndOfPeriod, body.Reason,
                    user.Identity!.Name!, user.IsInRole(Constants.Roles.ADMINISTRATORS));
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<LifecycleResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelSubscriptionRequest request, ISubscriptionService subscriptionService)
    {
        var subscription = await subscriptionService.CancelAsync(request.UserReference, request.IsAdmin, request.SubscriptionId, request.EndOfPeriod, request.Reason);
        return Results.Ok(LifecycleResponse.From(request.CorrelationId(), subscription));
    }
}
