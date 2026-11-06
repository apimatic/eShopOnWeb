using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

public class PlanHandleRequest : BaseRequest
{
    public string? PlanHandle { get; set; }
    /// <summary>Alias of PlanHandle.</summary>
    public string? Handle { get; set; }
}

public class NextBillingDateRequest : BaseRequest
{
    public string? Date { get; set; }
    /// <summary>Alias of Date.</summary>
    public string? NextBillingDate { get; set; }
}

/// <summary>
/// A shopper subscribes to a plan
/// </summary>
public class SubscribeEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = BillingUser.JWT)] async
            (PlanHandleRequest request, ClaimsPrincipal user, BillingService billing, CancellationToken ct) =>
            {
                var sub = await billing.SubscribeAsync(BillingUser.Name(user), request.PlanHandle ?? request.Handle, ct);
                return Results.Created($"api/subscriptions/{sub.Id}", sub);
            })
            .Produces<SubscriptionDto>(201)
            .WithTags("BillingEndpoints");
    }
}

/// <summary>
/// A shopper sees one of their subscriptions
/// </summary>
public class GetSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId}",
            [Authorize(AuthenticationSchemes = BillingUser.JWT)] async
            (long subscriptionId, ClaimsPrincipal user, BillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.GetSubscriptionAsync(BillingUser.Name(user), subscriptionId, ct)))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }
}

/// <summary>
/// Pause, resume, cancel and restart a subscription
/// </summary>
public class SubscriptionLifecycleEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/pause",
            [Authorize(AuthenticationSchemes = BillingUser.JWT)] async
            (long subscriptionId, ClaimsPrincipal user, BillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.PauseAsync(BillingUser.Name(user), subscriptionId, ct)))
            .Produces<SubscriptionDto>().WithTags("BillingEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId}/resume",
            [Authorize(AuthenticationSchemes = BillingUser.JWT)] async
            (long subscriptionId, ClaimsPrincipal user, BillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.ResumeAsync(BillingUser.Name(user), subscriptionId, ct)))
            .Produces<SubscriptionDto>().WithTags("BillingEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId}/cancel",
            [Authorize(AuthenticationSchemes = BillingUser.JWT)] async
            (long subscriptionId, ClaimsPrincipal user, BillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.CancelAsync(BillingUser.Name(user), subscriptionId, ct)))
            .Produces<SubscriptionDto>().WithTags("BillingEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId}/restart",
            [Authorize(AuthenticationSchemes = BillingUser.JWT)] async
            (long subscriptionId, ClaimsPrincipal user, BillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.RestartAsync(BillingUser.Name(user), subscriptionId, ct)))
            .Produces<SubscriptionDto>().WithTags("BillingEndpoints");
    }
}

/// <summary>
/// A shopper switches a subscription to another plan
/// </summary>
public class SwitchPlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/switch",
            [Authorize(AuthenticationSchemes = BillingUser.JWT)] async
            (long subscriptionId, PlanHandleRequest request, ClaimsPrincipal user, BillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.SwitchAsync(BillingUser.Name(user), subscriptionId, request.PlanHandle ?? request.Handle, ct)))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }
}

/// <summary>
/// A shopper sees the metered usage of a subscription on one day
/// </summary>
public class SubscriptionUsageEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId}/usage",
            [Authorize(AuthenticationSchemes = BillingUser.JWT)] async
            (long subscriptionId, string? date, ClaimsPrincipal user, BillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.GetUsageAsync(BillingUser.Name(user), subscriptionId, date, ct)))
            .Produces<DailyUsageDto>()
            .WithTags("BillingEndpoints");
    }
}

/// <summary>
/// Staff set the date a subscriber is next billed
/// </summary>
public class SetNextBillingDateEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/admin/subscriptions/{subscriptionId}/next-billing-date",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = BillingUser.JWT)] async
            (long subscriptionId, NextBillingDateRequest request, BillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.SetNextBillingDateAsync(subscriptionId, request.Date ?? request.NextBillingDate, ct)))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }
}
