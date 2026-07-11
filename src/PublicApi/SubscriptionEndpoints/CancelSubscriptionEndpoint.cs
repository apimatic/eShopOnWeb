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

/// <summary>UC4: cancels a subscription, either immediately or at the end of the current period.</summary>
public class CancelSubscriptionEndpoint : IEndpoint<IResult, CancelSubscriptionRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/cancel",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, CancelSubscriptionBody body, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                var isAdmin = user.IsInRole(Constants.Roles.ADMINISTRATORS);
                var request = new CancelSubscriptionRequest(user.Identity!.Name!, isAdmin, subscriptionId, body.EndOfPeriod, body.Reason);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<LifecycleResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelSubscriptionRequest request, ISubscriptionService subscriptionService)
    {
        var response = new LifecycleResponse(request.CorrelationId());

        var subscription = await subscriptionService.CancelAsync(
            request.ActingBuyerId, request.IsAdmin, request.SubscriptionId, request.EndOfPeriod, request.Reason);

        response.Subscription = SubscriptionDto.FromDomain(subscription);
        return Results.Ok(response);
    }
}
