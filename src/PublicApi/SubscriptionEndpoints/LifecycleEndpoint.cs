using System;
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

/// <summary>UC4: pause / resume / cancel / reactivate - one management surface, four lifecycle actions.</summary>
public class LifecycleEndpoint : IEndpoint<IResult, LifecycleRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/lifecycle",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (LifecycleRequest request, ISubscriptionService subscriptionService, ClaimsPrincipal user) =>
            {
                request.CallerReference = user.Identity!.Name!;
                request.CallerIsAdmin = user.IsInRole(Constants.Roles.ADMINISTRATORS);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<LifecycleResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(LifecycleRequest request, ISubscriptionService subscriptionService)
    {
        var (subscription, denied) = await SubscriptionAccess.ResolveAsync(
            subscriptionService, request.CallerReference, request.CallerIsAdmin, request.SubscriptionId);
        if (denied != null)
        {
            return denied;
        }

        var response = new LifecycleResponse(request.CorrelationId());

        var updated = request.Action switch
        {
            LifecycleAction.Pause => await subscriptionService.PauseAsync(subscription!.Id),
            LifecycleAction.Resume => await subscriptionService.ResumeAsync(subscription!.Id),
            LifecycleAction.Cancel => await subscriptionService.CancelAsync(subscription!.Id, request.EndOfPeriod, request.Reason),
            LifecycleAction.Reactivate => await subscriptionService.ReactivateAsync(subscription!.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(request.Action), request.Action, "Unknown lifecycle action.")
        };

        response.Subscription = updated.ToDto();
        return Results.Ok(response);
    }
}
