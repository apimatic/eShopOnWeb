using System;
using System.Globalization;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionEndpointHelpers
{
    public const string Scheme = JwtBearerDefaults.AuthenticationScheme;

    /// <summary>The caller's identity comes from the JWT, never from the request.</summary>
    public static string UserName(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name ?? user.FindFirst(ClaimTypes.Name)?.Value;
        return string.IsNullOrWhiteSpace(name) ? throw new BillingException(401, "The token carries no user name.") : name;
    }

    public static DateTime ParseDate(string? text, string what)
    {
        if (text != null && DateTime.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            return d;
        }
        throw BillingException.BadRequest($"{what} must be a date in the form YYYY-MM-DD.");
    }
}

/// <summary>Subscribes the signed-in shopper to a plan, without card details</summary>
public class SubscribeEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = SubscriptionEndpointHelpers.Scheme)] async
            (SubscribeRequest? request, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
            {
                var sub = await billing.SubscribeAsync(SubscriptionEndpointHelpers.UserName(user), request?.PlanHandle ?? request?.Handle, ct);
                return Results.Created($"api/subscriptions/{sub.Id}", sub);
            })
            .WithTags("SubscriptionEndpoints");
    }
}

/// <summary>One of the signed-in shopper's subscriptions, with whether it is paid</summary>
public class GetSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId}",
            [Authorize(AuthenticationSchemes = SubscriptionEndpointHelpers.Scheme)] async
            (int subscriptionId, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.GetSubscriptionAsync(SubscriptionEndpointHelpers.UserName(user), subscriptionId, ct)))
            .WithTags("SubscriptionEndpoints");
    }
}

/// <summary>Pauses a subscription</summary>
public class PauseSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/pause",
            [Authorize(AuthenticationSchemes = SubscriptionEndpointHelpers.Scheme)] async
            (int subscriptionId, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.PauseAsync(SubscriptionEndpointHelpers.UserName(user), subscriptionId, ct)))
            .WithTags("SubscriptionEndpoints");
    }
}

/// <summary>Resumes a paused subscription</summary>
public class ResumeSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/resume",
            [Authorize(AuthenticationSchemes = SubscriptionEndpointHelpers.Scheme)] async
            (int subscriptionId, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.ResumeAsync(SubscriptionEndpointHelpers.UserName(user), subscriptionId, ct)))
            .WithTags("SubscriptionEndpoints");
    }
}

/// <summary>Cancels a subscription</summary>
public class CancelSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/cancel",
            [Authorize(AuthenticationSchemes = SubscriptionEndpointHelpers.Scheme)] async
            (int subscriptionId, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.CancelAsync(SubscriptionEndpointHelpers.UserName(user), subscriptionId, ct)))
            .WithTags("SubscriptionEndpoints");
    }
}

/// <summary>Restarts a cancelled subscription</summary>
public class RestartSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/restart",
            [Authorize(AuthenticationSchemes = SubscriptionEndpointHelpers.Scheme)] async
            (int subscriptionId, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.RestartAsync(SubscriptionEndpointHelpers.UserName(user), subscriptionId, ct)))
            .WithTags("SubscriptionEndpoints");
    }
}

/// <summary>Switches a subscription to another plan, cancelled or not</summary>
public class SwitchPlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/switch",
            [Authorize(AuthenticationSchemes = SubscriptionEndpointHelpers.Scheme)] async
            (int subscriptionId, SwitchPlanRequest? request, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.SwitchAsync(SubscriptionEndpointHelpers.UserName(user), subscriptionId, request?.PlanHandle ?? request?.Handle, ct)))
            .WithTags("SubscriptionEndpoints");
    }
}

/// <summary>Metered usage recorded on a single day</summary>
public class SubscriptionUsageEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId}/usage",
            [Authorize(AuthenticationSchemes = SubscriptionEndpointHelpers.Scheme)] async
            (int subscriptionId, string? date, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
            {
                var day = SubscriptionEndpointHelpers.ParseDate(date, "date");
                return Results.Ok(await billing.UsageForDayAsync(SubscriptionEndpointHelpers.UserName(user), subscriptionId, day, ct));
            })
            .WithTags("SubscriptionEndpoints");
    }
}

/// <summary>Staff: sets the date a subscriber is next billed</summary>
public class SetNextBillingDateEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/admin/subscriptions/{subscriptionId}/next-billing-date",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = SubscriptionEndpointHelpers.Scheme)] async
            (int subscriptionId, SetNextBillingDateRequest? request, MaxioBillingService billing, CancellationToken ct) =>
            {
                var date = SubscriptionEndpointHelpers.ParseDate(request?.Date ?? request?.NextBillingDate, "date");
                return Results.Ok(await billing.SetNextBillingDateAsync(subscriptionId, date, ct));
            })
            .WithTags("SubscriptionEndpoints");
    }
}
