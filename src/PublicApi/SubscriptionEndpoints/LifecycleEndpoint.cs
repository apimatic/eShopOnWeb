using System;
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

/// <summary>
/// UC4 — the single lifecycle surface: pause, resume, cancel (immediately or at period end) and
/// reactivate. A customer may only act on their own subscription; an administrator may act on any.
/// </summary>
public class LifecycleEndpoint : IEndpoint<IResult, LifecycleRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/lifecycle",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (LifecycleRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.AuthenticatedUserName = SubscriptionEndpointResults.GetUserName(user);
                request.IsAdministrator = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<LifecycleResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(LifecycleRequest request, ISubscriptionService subscriptionService)
    {
        if (request.AuthenticatedUserName is null)
        {
            return Results.Unauthorized();
        }

        if (request.SubscriptionId <= 0)
        {
            return Results.BadRequest(new { error = "subscriptionId is required." });
        }

        var response = new LifecycleResponse(request.CorrelationId());

        try
        {
            var updated = request.IsAdministrator
                ? await subscriptionService.ApplyLifecycleActionForSubscriptionAsync(
                    request.SubscriptionId, request.Action, request.Timing, request.Reason)
                : await subscriptionService.ApplyLifecycleActionAsync(
                    request.AuthenticatedUserName, request.SubscriptionId, request.Action, request.Timing, request.Reason);

            response.Subscription = SubscriptionDto.From(updated);
        }
        catch (Exception ex) when (SubscriptionEndpointResults.IsExpected(ex))
        {
            return SubscriptionEndpointResults.FromException(ex);
        }

        return Results.Ok(response);
    }
}
