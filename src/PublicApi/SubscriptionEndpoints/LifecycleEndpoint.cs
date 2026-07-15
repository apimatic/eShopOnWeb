using System;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// UC4: one management surface for the four lifecycle actions. Customers act on their own
/// subscription; administrators may target any subscription.
/// </summary>
public class LifecycleEndpoint : IEndpoint<IResult, LifecycleActionRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/lifecycle",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (LifecycleActionRequest request, HttpContext httpContext, ISubscriptionService subscriptionService) =>
            {
                request.UserReference = httpContext.User.Identity!.Name!;
                request.IsAdmin = httpContext.User.IsInRole(Constants.Roles.ADMINISTRATORS);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<LifecycleActionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(LifecycleActionRequest request, ISubscriptionService subscriptionService)
    {
        var response = new LifecycleActionResponse(request.CorrelationId());

        if (!Enum.TryParse<LifecycleAction>(request.Action, ignoreCase: true, out var action))
        {
            throw new SubscriptionValidationException($"Unrecognized lifecycle action '{request.Action}'. Expected one of: Pause, Resume, Cancel, Reactivate.");
        }

        var subscription = action switch
        {
            LifecycleAction.Pause => await subscriptionService.PauseAsync(request.UserReference, request.SubscriptionId, request.IsAdmin),
            LifecycleAction.Resume => await subscriptionService.ResumeAsync(request.UserReference, request.SubscriptionId, request.IsAdmin),
            LifecycleAction.Cancel => await subscriptionService.CancelAsync(request.UserReference, request.SubscriptionId, request.EndOfPeriod, request.IsAdmin),
            LifecycleAction.Reactivate => await subscriptionService.ReactivateAsync(request.UserReference, request.SubscriptionId, request.IsAdmin),
            _ => throw new SubscriptionValidationException($"Unrecognized lifecycle action '{request.Action}'.")
        };

        response.Subscription = SubscriptionDto.FromDomain(subscription);
        return Results.Ok(response);
    }
}
