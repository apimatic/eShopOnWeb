using System;
using System.Globalization;
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

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

internal static class BillingEndpointHelpers
{
    public const string SHOPPER_TAG = "SubscriptionEndpoints";

    /// <summary>The caller's identity always comes from the JWT.</summary>
    public static string UserName(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
            throw new BillingException(System.Net.HttpStatusCode.Unauthorized, "The token carries no user identity.");
        return name;
    }
}

public class SubscriptionRequestBody : BaseRequest
{
    /// <summary>The handle of the plan.</summary>
    public string? PlanHandle { get; set; }
}

/// <summary>
/// Lists the plans a shopper can subscribe to
/// </summary>
public class ListPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.ListPlansAsync(ct)))
            .Produces<System.Collections.Generic.IReadOnlyList<PlanDto>>()
            .WithTags("PlanEndpoints");
    }
}

/// <summary>
/// Subscribes the signed-in shopper to a plan, without card details
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscriptionRequestBody request, ClaimsPrincipal user, IMaxioBillingService billing, CancellationToken ct) =>
            {
                var subscription = await billing.SubscribeAsync(BillingEndpointHelpers.UserName(user), request.PlanHandle ?? string.Empty, ct);
                return Results.Created($"api/subscriptions/{subscription.Id}", subscription);
            })
            .Produces<SubscriptionDto>(StatusCodes.Status201Created)
            .WithTags(BillingEndpointHelpers.SHOPPER_TAG);
    }
}

/// <summary>
/// Shows one of the shopper's subscriptions
/// </summary>
public class GetSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, IMaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.GetSubscriptionAsync(BillingEndpointHelpers.UserName(user), subscriptionId, ct)))
            .Produces<SubscriptionDto>()
            .WithTags(BillingEndpointHelpers.SHOPPER_TAG);
    }
}

/// <summary>
/// Pauses a subscription
/// </summary>
public class PauseSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/pause",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, IMaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.PauseAsync(BillingEndpointHelpers.UserName(user), subscriptionId, ct)))
            .Produces<SubscriptionDto>()
            .WithTags(BillingEndpointHelpers.SHOPPER_TAG);
    }
}

/// <summary>
/// Resumes a paused subscription
/// </summary>
public class ResumeSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/resume",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, IMaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.ResumeAsync(BillingEndpointHelpers.UserName(user), subscriptionId, ct)))
            .Produces<SubscriptionDto>()
            .WithTags(BillingEndpointHelpers.SHOPPER_TAG);
    }
}

/// <summary>
/// Cancels a subscription
/// </summary>
public class CancelSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/cancel",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, IMaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.CancelAsync(BillingEndpointHelpers.UserName(user), subscriptionId, ct)))
            .Produces<SubscriptionDto>()
            .WithTags(BillingEndpointHelpers.SHOPPER_TAG);
    }
}

/// <summary>
/// Restarts a canceled subscription
/// </summary>
public class RestartSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/restart",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, IMaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.RestartAsync(BillingEndpointHelpers.UserName(user), subscriptionId, ct)))
            .Produces<SubscriptionDto>()
            .WithTags(BillingEndpointHelpers.SHOPPER_TAG);
    }
}

/// <summary>
/// Switches a subscription to another plan
/// </summary>
public class SwitchSubscriptionPlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/switch",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, SubscriptionRequestBody request, ClaimsPrincipal user, IMaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.SwitchPlanAsync(BillingEndpointHelpers.UserName(user), subscriptionId, request.PlanHandle ?? string.Empty, ct)))
            .Produces<SubscriptionDto>()
            .WithTags(BillingEndpointHelpers.SHOPPER_TAG);
    }
}

/// <summary>
/// Shows the metered usage of a subscription on one day
/// </summary>
public class GetSubscriptionUsageEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, string? date, ClaimsPrincipal user, IMaxioBillingService billing, CancellationToken ct) =>
            {
                if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                    throw BillingException.BadRequest("The date query parameter is required and must be formatted YYYY-MM-DD.");
                return Results.Ok(await billing.GetUsageAsync(BillingEndpointHelpers.UserName(user), subscriptionId, day, ct));
            })
            .Produces<UsageDto>()
            .WithTags(BillingEndpointHelpers.SHOPPER_TAG);
    }
}
