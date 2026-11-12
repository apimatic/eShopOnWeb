using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.MaxioEndpoints;

public record SubscribeRequest(string? PlanHandle);

public record SwitchPlanRequest(string? PlanHandle);

/// <summary>
/// A signed-in shopper's own subscriptions: subscribe, view, pause/resume, cancel/restart, switch plan, daily usage.
/// The caller is the JWT's name claim; a subscription that is not theirs answers 404.
/// </summary>
public class SubscriptionEndpoints : IEndpoint
{
    private const string Scheme = JwtBearerDefaults.AuthenticationScheme;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = Scheme)]
            (SubscribeRequest request, ClaimsPrincipal user, MaxioBillingService billing, HttpContext http) =>
                With(user, billing, http, (name, ct) => billing.SubscribeAsync(name, request.PlanHandle, ct),
                    r => Results.Created($"api/subscriptions/{r.SubscriptionId}", r)))
            .Produces<SubscriptionDto>(201)
            .WithTags("MaxioEndpoints");

        app.MapGet("api/subscriptions/{subscriptionId:int}",
            [Authorize(AuthenticationSchemes = Scheme)]
            (int subscriptionId, ClaimsPrincipal user, MaxioBillingService billing, HttpContext http) =>
                With(user, billing, http, (name, ct) => billing.GetSubscriptionAsync(name, subscriptionId, ct)))
            .Produces<SubscriptionDto>()
            .WithTags("MaxioEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId:int}/pause",
            [Authorize(AuthenticationSchemes = Scheme)]
            (int subscriptionId, ClaimsPrincipal user, MaxioBillingService billing, HttpContext http) =>
                With(user, billing, http, (name, ct) => billing.PauseAsync(name, subscriptionId, ct)))
            .Produces<SubscriptionDto>().WithTags("MaxioEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId:int}/resume",
            [Authorize(AuthenticationSchemes = Scheme)]
            (int subscriptionId, ClaimsPrincipal user, MaxioBillingService billing, HttpContext http) =>
                With(user, billing, http, (name, ct) => billing.ResumeAsync(name, subscriptionId, ct)))
            .Produces<SubscriptionDto>().WithTags("MaxioEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId:int}/cancel",
            [Authorize(AuthenticationSchemes = Scheme)]
            (int subscriptionId, ClaimsPrincipal user, MaxioBillingService billing, HttpContext http) =>
                With(user, billing, http, (name, ct) => billing.CancelAsync(name, subscriptionId, ct)))
            .Produces<SubscriptionDto>().WithTags("MaxioEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId:int}/restart",
            [Authorize(AuthenticationSchemes = Scheme)]
            (int subscriptionId, ClaimsPrincipal user, MaxioBillingService billing, HttpContext http) =>
                With(user, billing, http, (name, ct) => billing.RestartAsync(name, subscriptionId, ct)))
            .Produces<SubscriptionDto>().WithTags("MaxioEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId:int}/switch",
            [Authorize(AuthenticationSchemes = Scheme)]
            (int subscriptionId, SwitchPlanRequest request, ClaimsPrincipal user, MaxioBillingService billing, HttpContext http) =>
                With(user, billing, http, (name, ct) => billing.SwitchAsync(name, subscriptionId, request.PlanHandle, ct)))
            .Produces<SubscriptionDto>().WithTags("MaxioEndpoints");

        app.MapGet("api/subscriptions/{subscriptionId:int}/usage",
            [Authorize(AuthenticationSchemes = Scheme)]
            (int subscriptionId, string? date, ClaimsPrincipal user, MaxioBillingService billing, HttpContext http) =>
                With(user, billing, http, (name, ct) => billing.GetUsageAsync(name, subscriptionId, date, ct)))
            .Produces<UsageDayDto>().WithTags("MaxioEndpoints");
    }

    private static Task<IResult> With<T>(ClaimsPrincipal user, MaxioBillingService billing, HttpContext http,
        Func<string, CancellationToken, Task<T>> work, Func<T, IResult>? ok = null)
    {
        var name = MaxioEndpointSupport.UserName(user);
        if (string.IsNullOrWhiteSpace(name))
            return Task.FromResult(MaxioEndpointSupport.Unauthenticated());
        return MaxioEndpointSupport.RunAsync(billing, http, ct => work(name, ct), ok);
    }
}
