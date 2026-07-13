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

/// <summary>UC4: resume a paused subscription.</summary>
public class ResumeSubscriptionEndpoint : IEndpoint<IResult, LifecycleActionRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/resume",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long subscriptionId, HttpContext httpContext, ISubscriptionService subscriptionService) =>
            {
                var customerReference = httpContext.User.Identity?.Name ?? string.Empty;
                var actingAsAdmin = httpContext.User.IsInRole(Constants.Roles.ADMINISTRATORS);
                var request = new LifecycleActionRequest(subscriptionId, customerReference, actingAsAdmin);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<LifecycleSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(LifecycleActionRequest request, ISubscriptionService subscriptionService)
    {
        var response = new LifecycleSubscriptionResponse(request.CorrelationId());
        var subscription = await subscriptionService.ResumeSubscriptionAsync(request.CustomerReference, request.ActingAsAdmin, request.SubscriptionId);
        response.Subscription = subscription.ToDto();
        return Results.Ok(response);
    }
}
