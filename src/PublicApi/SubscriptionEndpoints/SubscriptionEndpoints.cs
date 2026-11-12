using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public record SubscribeRequest(string? PlanHandle);
public record SwitchPlanRequest(string? PlanHandle);
public record SetNextBillingDateRequest(string? Date);

internal static class Caller
{
    public const string JWT = JwtBearerDefaults.AuthenticationScheme;

    /// <summary>The caller's identity comes from the JWT, never from the request.</summary>
    public static string Name(ClaimsPrincipal user) => user.Identity?.Name
        ?? throw new Billing.BillingException(401, "The token does not identify a user.");
}

/// <summary>
/// A signed-in shopper subscribes to a plan, without giving card details
/// </summary>
public class SubscribeEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = Caller.JWT)] async
            (SubscribeRequest request, ClaimsPrincipal user, IBillingService billing, CancellationToken ct) => await HandleAsync(request, user, billing, ct))
            .Produces<SubscriptionDto>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ClaimsPrincipal user, IBillingService billing, CancellationToken ct)
    {
        var sub = await billing.SubscribeAsync(Caller.Name(user), request.PlanHandle, ct);
        return Results.Created($"api/subscriptions/{sub.SubscriptionId}", sub);
    }
}

/// <summary>
/// A shopper sees one of their subscriptions
/// </summary>
public class GetSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId:int}",
            [Authorize(AuthenticationSchemes = Caller.JWT)] async
            (int subscriptionId, ClaimsPrincipal user, IBillingService billing, CancellationToken ct) => await HandleAsync(subscriptionId, user, billing, ct))
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(int subscriptionId, ClaimsPrincipal user, IBillingService billing, CancellationToken ct)
        => Results.Ok(await billing.GetSubscriptionAsync(Caller.Name(user), subscriptionId, ct));
}

/// <summary>
/// A shopper pauses a subscription
/// </summary>
public class PauseSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/pause",
            [Authorize(AuthenticationSchemes = Caller.JWT)] async
            (int subscriptionId, ClaimsPrincipal user, IBillingService billing, CancellationToken ct) => await HandleAsync(subscriptionId, user, billing, ct))
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(int subscriptionId, ClaimsPrincipal user, IBillingService billing, CancellationToken ct)
        => Results.Ok(await billing.PauseAsync(Caller.Name(user), subscriptionId, ct));
}

/// <summary>
/// A shopper resumes a paused subscription
/// </summary>
public class ResumeSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/resume",
            [Authorize(AuthenticationSchemes = Caller.JWT)] async
            (int subscriptionId, ClaimsPrincipal user, IBillingService billing, CancellationToken ct) => await HandleAsync(subscriptionId, user, billing, ct))
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(int subscriptionId, ClaimsPrincipal user, IBillingService billing, CancellationToken ct)
        => Results.Ok(await billing.ResumeAsync(Caller.Name(user), subscriptionId, ct));
}

/// <summary>
/// A shopper cancels a subscription
/// </summary>
public class CancelSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/cancel",
            [Authorize(AuthenticationSchemes = Caller.JWT)] async
            (int subscriptionId, ClaimsPrincipal user, IBillingService billing, CancellationToken ct) => await HandleAsync(subscriptionId, user, billing, ct))
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(int subscriptionId, ClaimsPrincipal user, IBillingService billing, CancellationToken ct)
        => Results.Ok(await billing.CancelAsync(Caller.Name(user), subscriptionId, ct));
}

/// <summary>
/// A shopper restarts a subscription they cancelled
/// </summary>
public class RestartSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/restart",
            [Authorize(AuthenticationSchemes = Caller.JWT)] async
            (int subscriptionId, ClaimsPrincipal user, IBillingService billing, CancellationToken ct) => await HandleAsync(subscriptionId, user, billing, ct))
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(int subscriptionId, ClaimsPrincipal user, IBillingService billing, CancellationToken ct)
        => Results.Ok(await billing.RestartAsync(Caller.Name(user), subscriptionId, ct));
}

/// <summary>
/// A shopper switches a subscription to another plan
/// </summary>
public class SwitchPlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/switch",
            [Authorize(AuthenticationSchemes = Caller.JWT)] async
            (int subscriptionId, SwitchPlanRequest request, ClaimsPrincipal user, IBillingService billing, CancellationToken ct) => await HandleAsync(subscriptionId, request, user, billing, ct))
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(int subscriptionId, SwitchPlanRequest request, ClaimsPrincipal user, IBillingService billing, CancellationToken ct)
        => Results.Ok(await billing.SwitchPlanAsync(Caller.Name(user), subscriptionId, request.PlanHandle, ct));
}

/// <summary>
/// A shopper sees their metered usage on one day
/// </summary>
public class SubscriptionUsageEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId:int}/usage",
            [Authorize(AuthenticationSchemes = Caller.JWT)] async
            (int subscriptionId, string? date, ClaimsPrincipal user, IBillingService billing, CancellationToken ct) => await HandleAsync(subscriptionId, date, user, billing, ct))
            .Produces<UsageDto>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(int subscriptionId, string? date, ClaimsPrincipal user, IBillingService billing, CancellationToken ct)
        => Results.Ok(await billing.GetUsageAsync(Caller.Name(user), subscriptionId, date, ct));
}

/// <summary>
/// Staff set the date a subscriber is next billed
/// </summary>
public class SetNextBillingDateEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/admin/subscriptions/{subscriptionId:int}/next-billing-date",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = Caller.JWT)] async
            (int subscriptionId, SetNextBillingDateRequest request, IBillingService billing, CancellationToken ct) => await HandleAsync(subscriptionId, request, billing, ct))
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(int subscriptionId, SetNextBillingDateRequest request, IBillingService billing, CancellationToken ct)
        => Results.Ok(await billing.SetNextBillingDateAsync(subscriptionId, request.Date, ct));
}
