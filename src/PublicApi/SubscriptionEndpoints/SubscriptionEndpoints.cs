using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest
{
    /// <summary>The handle of the plan to subscribe to.</summary>
    public string? PlanHandle { get; set; }

    /// <summary>Alias of <see cref="PlanHandle"/>.</summary>
    public string? Handle { get; set; }

    public string? ResolvedHandle => string.IsNullOrWhiteSpace(PlanHandle) ? Handle : PlanHandle;
}

public class SubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }

    /// <summary>True when the shopper owes nothing on the subscription, false when money is still owed.</summary>
    public bool Paid { get; set; }
    public long BalanceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }

    public static SubscriptionDto From(MaxioBillingService.SubscriptionInfo s) => new()
    {
        SubscriptionId = s.Id,
        State = s.State,
        PlanHandle = s.PlanHandle,
        PlanName = s.PlanName,
        Paid = s.Paid,
        BalanceInCents = s.BalanceInCents,
        CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
        CanceledAt = s.CanceledAt
    };
}

/// <summary>
/// Subscribes the signed-in shopper to a plan, without card details
/// </summary>
public class SubscribeEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, HttpContext http, MaxioBillingService billing, OperationClaimStore claims) =>
                await HandleAsync(request, http, billing, claims))
            .Produces<SubscriptionDto>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscribeRequest request, HttpContext http, MaxioBillingService billing, OperationClaimStore claims) =>
        BillingEndpointHelper.RunForCallerAsync(http, async (user, ct) =>
        {
            var handle = request.ResolvedHandle;
            if (string.IsNullOrWhiteSpace(handle))
                return BillingEndpointHelper.Failure(400, "A plan handle is required.");

            // Claim first (the store refuses a second concurrent claim), then call billing, then release.
            var claimKey = $"subscribe:{user}:{handle}";
            if (!await claims.TryClaimAsync(claimKey, ct))
                return BillingEndpointHelper.Failure(409, "A subscription to this plan is already being created.");
            try
            {
                var sub = await billing.SubscribeAsync(user, handle, Guid.NewGuid().ToString("N"), ct);
                return Results.Created($"api/subscriptions/{sub.Id}", SubscriptionDto.From(sub));
            }
            finally
            {
                await claims.ReleaseAsync(claimKey);
            }
        });
}

/// <summary>
/// Shows one of the shopper's subscriptions
/// </summary>
public class SubscriptionGetEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, HttpContext http, MaxioBillingService billing) =>
                await HandleAsync(subscriptionId, http, billing))
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(int subscriptionId, HttpContext http, MaxioBillingService billing) =>
        BillingEndpointHelper.RunForCallerAsync(http, async (user, ct) =>
            Results.Ok(SubscriptionDto.From(await billing.GetSubscriptionAsync(user, subscriptionId, ct))));
}

/// <summary>
/// Pauses a subscription
/// </summary>
public class SubscriptionPauseEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/pause",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, HttpContext http, MaxioBillingService billing) =>
                await HandleAsync(subscriptionId, http, billing))
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(int subscriptionId, HttpContext http, MaxioBillingService billing) =>
        BillingEndpointHelper.RunForCallerAsync(http, async (user, ct) =>
            Results.Ok(SubscriptionDto.From(await billing.PauseAsync(user, subscriptionId, ct))));
}

/// <summary>
/// Resumes a paused subscription
/// </summary>
public class SubscriptionResumeEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/resume",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, HttpContext http, MaxioBillingService billing) =>
                await HandleAsync(subscriptionId, http, billing))
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(int subscriptionId, HttpContext http, MaxioBillingService billing) =>
        BillingEndpointHelper.RunForCallerAsync(http, async (user, ct) =>
            Results.Ok(SubscriptionDto.From(await billing.ResumeAsync(user, subscriptionId, ct))));
}

/// <summary>
/// Cancels a subscription
/// </summary>
public class SubscriptionCancelEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/cancel",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, HttpContext http, MaxioBillingService billing) =>
                await HandleAsync(subscriptionId, http, billing))
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(int subscriptionId, HttpContext http, MaxioBillingService billing) =>
        BillingEndpointHelper.RunForCallerAsync(http, async (user, ct) =>
            Results.Ok(SubscriptionDto.From(await billing.CancelAsync(user, subscriptionId, ct))));
}

/// <summary>
/// Restarts a cancelled subscription
/// </summary>
public class SubscriptionRestartEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/restart",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, HttpContext http, MaxioBillingService billing) =>
                await HandleAsync(subscriptionId, http, billing))
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(int subscriptionId, HttpContext http, MaxioBillingService billing) =>
        BillingEndpointHelper.RunForCallerAsync(http, async (user, ct) =>
            Results.Ok(SubscriptionDto.From(await billing.RestartAsync(user, subscriptionId, ct))));
}

/// <summary>
/// Switches a subscription (cancelled ones included) to another plan
/// </summary>
public class SubscriptionSwitchEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/switch",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, SubscribeRequest request, HttpContext http, MaxioBillingService billing) =>
                await HandleAsync(subscriptionId, request, http, billing))
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(int subscriptionId, SubscribeRequest request, HttpContext http, MaxioBillingService billing) =>
        BillingEndpointHelper.RunForCallerAsync(http, async (user, ct) =>
        {
            var handle = request.ResolvedHandle;
            if (string.IsNullOrWhiteSpace(handle))
                return BillingEndpointHelper.Failure(400, "A plan handle is required.");
            return Results.Ok(SubscriptionDto.From(await billing.SwitchPlanAsync(user, subscriptionId, handle, ct)));
        });
}

/// <summary>
/// Shows the shopper's metered usage on one day
/// </summary>
public class SubscriptionUsageEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId:int}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, string? date, HttpContext http, MaxioBillingService billing) =>
                await HandleAsync(subscriptionId, date, http, billing))
            .Produces<MaxioBillingService.UsageResult>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(int subscriptionId, string? date, HttpContext http, MaxioBillingService billing) =>
        BillingEndpointHelper.RunForCallerAsync(http, async (user, ct) =>
        {
            if (!BillingEndpointHelper.TryParseDate(date, out var day))
                return BillingEndpointHelper.Failure(400, "The date query parameter is required, as YYYY-MM-DD.");
            return Results.Ok(await billing.GetUsageAsync(user, subscriptionId, day, ct));
        });
}
