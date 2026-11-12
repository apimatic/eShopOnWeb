using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public class SwitchPlanRequest : BaseRequest
{
    public string? PlanHandle { get; set; }
}

public class SubscribeRequest : BaseRequest
{
    public string? PlanHandle { get; set; }
}

/// <summary>
/// The signed-in shopper's billing: plans, subscriptions, pause/resume, cancel/restart, switch and daily usage.
/// The caller is whoever the JWT says they are; nothing about identity is read from the request.
/// </summary>
public class SubscriptionEndpoints : IEndpoint
{
    private const string Jwt = JwtBearerDefaults.AuthenticationScheme;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/plans",
            [Authorize(AuthenticationSchemes = Jwt)] async (MaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.ListPlansAsync(ct)))
            .WithTags("SubscriptionEndpoints");

        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = Jwt)] async (SubscribeRequest request, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
            {
                var sub = await billing.SubscribeAsync(UserName(user), request.PlanHandle, ct);
                return Results.Created($"api/subscriptions/{sub.Id}", sub);
            })
            .WithTags("SubscriptionEndpoints");

        app.MapGet("api/subscriptions/{subscriptionId:int}",
            [Authorize(AuthenticationSchemes = Jwt)] async (int subscriptionId, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.GetSubscriptionAsync(subscriptionId, UserName(user), ct)))
            .WithTags("SubscriptionEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId:int}/pause",
            [Authorize(AuthenticationSchemes = Jwt)] async (int subscriptionId, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.PauseAsync(subscriptionId, UserName(user), ct)))
            .WithTags("SubscriptionEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId:int}/resume",
            [Authorize(AuthenticationSchemes = Jwt)] async (int subscriptionId, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.ResumeAsync(subscriptionId, UserName(user), ct)))
            .WithTags("SubscriptionEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId:int}/cancel",
            [Authorize(AuthenticationSchemes = Jwt)] async (int subscriptionId, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.CancelAsync(subscriptionId, UserName(user), ct)))
            .WithTags("SubscriptionEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId:int}/restart",
            [Authorize(AuthenticationSchemes = Jwt)] async (int subscriptionId, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.RestartAsync(subscriptionId, UserName(user), ct)))
            .WithTags("SubscriptionEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId:int}/switch",
            [Authorize(AuthenticationSchemes = Jwt)] async (int subscriptionId, SwitchPlanRequest request, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.SwitchAsync(subscriptionId, UserName(user), request.PlanHandle, ct)))
            .WithTags("SubscriptionEndpoints");

        app.MapGet("api/subscriptions/{subscriptionId:int}/usage",
            [Authorize(AuthenticationSchemes = Jwt)] async (int subscriptionId, string? date, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.GetUsageAsync(subscriptionId, UserName(user), date, ct)))
            .WithTags("SubscriptionEndpoints");
    }

    private static string UserName(ClaimsPrincipal user) =>
        user.Identity?.Name ?? throw new BillingException(401, "The token does not identify a user.");
}
