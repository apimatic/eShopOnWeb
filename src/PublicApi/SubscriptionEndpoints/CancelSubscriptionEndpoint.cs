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

/// <summary>UC4: cancel a subscription, immediately or at the end of the current period.</summary>
public class CancelSubscriptionEndpoint : IEndpoint<IResult, CancelSubscriptionRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/cancel",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long subscriptionId, CancelSubscriptionBody body, HttpContext httpContext, ISubscriptionService subscriptionService) =>
            {
                var customerReference = httpContext.User.Identity?.Name ?? string.Empty;
                var actingAsAdmin = httpContext.User.IsInRole(Constants.Roles.ADMINISTRATORS);
                var request = new CancelSubscriptionRequest(subscriptionId, customerReference, actingAsAdmin, body);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<LifecycleSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelSubscriptionRequest request, ISubscriptionService subscriptionService)
    {
        var response = new LifecycleSubscriptionResponse(request.CorrelationId());
        var subscription = await subscriptionService.CancelSubscriptionAsync(
            request.CustomerReference, request.ActingAsAdmin, request.SubscriptionId, request.EndOfPeriod, request.Reason);
        response.Subscription = subscription.ToDto();
        return Results.Ok(response);
    }
}
