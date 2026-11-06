using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanHandleRequest
{
    public string? PlanHandle { get; set; }
}

internal static class Shopper
{
    public const string TAG = "SubscriptionEndpoints";

    /// <summary>The caller's identity comes from the JWT.</summary>
    public static string? Name(ClaimsPrincipal user) => user.Identity?.Name;
}

/// <summary>Lists the plans a shopper can subscribe to</summary>
public class ListPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (BillingService billing) =>
                Results.Ok(await billing.ListPlansAsync()))
            .WithTags(Shopper.TAG);
    }
}

/// <summary>Subscribes the signed-in shopper to a plan (no card details)</summary>
public class SubscribeEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (PlanHandleRequest request, ClaimsPrincipal user, BillingService billing) =>
            {
                var name = Shopper.Name(user);
                if (name == null) return Results.Unauthorized();
                var sub = await billing.SubscribeAsync(name, request.PlanHandle);
                return Results.Created($"api/subscriptions/{sub.Id}", sub);
            })
            .WithTags(Shopper.TAG);
    }
}

/// <summary>Shows one of the shopper's subscriptions</summary>
public class GetSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId:long}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (long subscriptionId, ClaimsPrincipal user, BillingService billing) =>
            {
                var name = Shopper.Name(user);
                if (name == null) return Results.Unauthorized();
                return Results.Ok(await billing.GetAsync(name, subscriptionId));
            })
            .WithTags(Shopper.TAG);
    }
}

public class PauseSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:long}/pause",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (long subscriptionId, ClaimsPrincipal user, BillingService billing) =>
            {
                var name = Shopper.Name(user);
                if (name == null) return Results.Unauthorized();
                return Results.Ok(await billing.PauseAsync(name, subscriptionId));
            })
            .WithTags(Shopper.TAG);
    }
}

public class ResumeSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:long}/resume",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (long subscriptionId, ClaimsPrincipal user, BillingService billing) =>
            {
                var name = Shopper.Name(user);
                if (name == null) return Results.Unauthorized();
                return Results.Ok(await billing.ResumeAsync(name, subscriptionId));
            })
            .WithTags(Shopper.TAG);
    }
}

public class CancelSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:long}/cancel",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (long subscriptionId, ClaimsPrincipal user, BillingService billing) =>
            {
                var name = Shopper.Name(user);
                if (name == null) return Results.Unauthorized();
                return Results.Ok(await billing.CancelAsync(name, subscriptionId));
            })
            .WithTags(Shopper.TAG);
    }
}

public class RestartSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:long}/restart",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (long subscriptionId, ClaimsPrincipal user, BillingService billing) =>
            {
                var name = Shopper.Name(user);
                if (name == null) return Results.Unauthorized();
                return Results.Ok(await billing.RestartAsync(name, subscriptionId));
            })
            .WithTags(Shopper.TAG);
    }
}

public class SwitchSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:long}/switch",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (long subscriptionId, PlanHandleRequest request, ClaimsPrincipal user, BillingService billing) =>
            {
                var name = Shopper.Name(user);
                if (name == null) return Results.Unauthorized();
                return Results.Ok(await billing.SwitchAsync(name, subscriptionId, request.PlanHandle));
            })
            .WithTags(Shopper.TAG);
    }
}

/// <summary>Metered usage on a single day</summary>
public class SubscriptionUsageEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId:long}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (long subscriptionId, string? date, ClaimsPrincipal user, BillingService billing) =>
            {
                var name = Shopper.Name(user);
                if (name == null) return Results.Unauthorized();
                return Results.Ok(await billing.UsageAsync(name, subscriptionId, date));
            })
            .WithTags(Shopper.TAG);
    }
}
