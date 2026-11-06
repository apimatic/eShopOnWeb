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

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public record SubscribeRequest(string? PlanHandle);

public record SwitchPlanRequest(string? PlanHandle);

internal static class CallerExtensions
{
    /// <summary>The caller's identity always comes from the JWT.</summary>
    public static string Caller(this ClaimsPrincipal user)
    {
        var name = user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ApiException(401, "The token does not identify a user.");
        }

        return name;
    }
}

internal static class Auth
{
    public const string SCHEME = JwtBearerDefaults.AuthenticationScheme;
    public const string ADMIN = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;
}

/// <summary>Lists the plans a shopper can subscribe to</summary>
public class ListPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/plans",
            [Authorize(AuthenticationSchemes = Auth.SCHEME)] async (BillingService billing, CancellationToken ct)
                => Results.Ok(await billing.ListPlansAsync(ct)))
            .Produces<System.Collections.Generic.List<PlanDto>>()
            .WithTags("SubscriptionEndpoints");
    }
}

/// <summary>Subscribes the signed-in shopper to a plan</summary>
public class SubscribeEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = Auth.SCHEME)] async (SubscribeRequest request, ClaimsPrincipal user, BillingService billing, CancellationToken ct) =>
            {
                var sub = await billing.SubscribeAsync(user.Caller(), request.PlanHandle ?? "", ct);
                return Results.Created($"api/subscriptions/{sub.Id}", sub);
            })
            .Produces<SubscriptionDto>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints");
    }
}

/// <summary>Shows one of the shopper's subscriptions</summary>
public class GetSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId:long}",
            [Authorize(AuthenticationSchemes = Auth.SCHEME)] async (long subscriptionId, ClaimsPrincipal user, BillingService billing, CancellationToken ct)
                => Results.Ok(await billing.GetSubscriptionAsync(user.Caller(), subscriptionId, ct)))
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }
}

/// <summary>Pauses and resumes a subscription</summary>
public class PauseResumeSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:long}/pause",
            [Authorize(AuthenticationSchemes = Auth.SCHEME)] async (long subscriptionId, ClaimsPrincipal user, BillingService billing, CancellationToken ct)
                => Results.Ok(await billing.PauseAsync(user.Caller(), subscriptionId, ct)))
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId:long}/resume",
            [Authorize(AuthenticationSchemes = Auth.SCHEME)] async (long subscriptionId, ClaimsPrincipal user, BillingService billing, CancellationToken ct)
                => Results.Ok(await billing.ResumeAsync(user.Caller(), subscriptionId, ct)))
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }
}

/// <summary>Cancels and restarts a subscription</summary>
public class CancelRestartSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:long}/cancel",
            [Authorize(AuthenticationSchemes = Auth.SCHEME)] async (long subscriptionId, ClaimsPrincipal user, BillingService billing, CancellationToken ct)
                => Results.Ok(await billing.CancelAsync(user.Caller(), subscriptionId, ct)))
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId:long}/restart",
            [Authorize(AuthenticationSchemes = Auth.SCHEME)] async (long subscriptionId, ClaimsPrincipal user, BillingService billing, CancellationToken ct)
                => Results.Ok(await billing.RestartAsync(user.Caller(), subscriptionId, ct)))
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }
}

/// <summary>Switches a subscription to another plan</summary>
public class SwitchPlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:long}/switch",
            [Authorize(AuthenticationSchemes = Auth.SCHEME)] async (long subscriptionId, SwitchPlanRequest request, ClaimsPrincipal user, BillingService billing, CancellationToken ct)
                => Results.Ok(await billing.SwitchPlanAsync(user.Caller(), subscriptionId, request.PlanHandle ?? "", ct)))
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }
}

/// <summary>Metered usage on a single day</summary>
public class SubscriptionUsageEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId:long}/usage",
            [Authorize(AuthenticationSchemes = Auth.SCHEME)] async (long subscriptionId, string? date, ClaimsPrincipal user, BillingService billing, CancellationToken ct) =>
            {
                if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                {
                    throw new ApiException(400, "date is required and must look like YYYY-MM-DD.");
                }

                return Results.Ok(await billing.GetUsageAsync(user.Caller(), subscriptionId, day, ct));
            })
            .Produces<UsageDto>()
            .WithTags("SubscriptionEndpoints");
    }
}

public record CreatePlanRequest(string? Name, decimal? MonthlyPrice, string? Brand, int? BrandId);

public record CurrencyPriceRequest(string? Currency, decimal? Price);

public record NextBillingDateRequest(string? Date);

/// <summary>Staff adds a plan</summary>
public class CreatePlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans",
            [Authorize(Roles = Auth.ADMIN, AuthenticationSchemes = Auth.SCHEME)] async (CreatePlanRequest request, BillingService billing, CancellationToken ct) =>
            {
                var plan = await billing.CreatePlanAsync(request.Name, request.MonthlyPrice, request.Brand, request.BrandId, ct);
                return Results.Created($"api/plans/{plan.Handle}", plan);
            })
            .Produces<PlanDto>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints");
    }
}

/// <summary>Staff prices a plan in another currency</summary>
public class AddCurrencyPriceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans/{planId:long}/currency-prices",
            [Authorize(Roles = Auth.ADMIN, AuthenticationSchemes = Auth.SCHEME)] async (long planId, CurrencyPriceRequest request, BillingService billing, CancellationToken ct) =>
            {
                var result = await billing.AddCurrencyPriceAsync(planId, request.Currency, request.Price, ct);
                return Results.Created($"api/admin/plans/{planId}/currency-prices/{result.Currency}", result);
            })
            .Produces<CurrencyPriceDto>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints");
    }
}

/// <summary>Staff sets the next billing date of a subscription</summary>
public class SetNextBillingDateEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/admin/subscriptions/{subscriptionId:long}/next-billing-date",
            [Authorize(Roles = Auth.ADMIN, AuthenticationSchemes = Auth.SCHEME)] async (long subscriptionId, NextBillingDateRequest request, BillingService billing, CancellationToken ct) =>
            {
                if (!DateOnly.TryParseExact(request.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                {
                    throw new ApiException(400, "date is required and must look like YYYY-MM-DD.");
                }

                return Results.Ok(await billing.SetNextBillingDateAsync(subscriptionId, day, ct));
            })
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }
}
