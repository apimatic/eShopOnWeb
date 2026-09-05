using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>JWT-protected subscription enrollment and account endpoints.</summary>
public sealed class SubscriptionEndpoints : IEndpoint<IResult>
{
    // Route handlers below have distinct request/response shapes; this member satisfies
    // the endpoint-discovery contract and is not mapped as an HTTP route.
    public Task<IResult> HandleAsync() => Task.FromResult(Results.NotFound());

    public void AddRoute(IEndpointRouteBuilder app)
    {
        var plans = app.MapGet("api/subscription-plans", GetPlansAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<IReadOnlyList<SubscriptionPlanResponse>>()
            .WithTags("Subscriptions");

        var subscribe = app.MapPost("api/subscriptions", SubscribeAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<SubscriptionResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("Subscriptions");

        var mine = app.MapGet("api/my-subscriptions", GetMySubscriptionsAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<IReadOnlyList<SubscriptionResponse>>()
            .WithTags("Subscriptions");
    }

    private static async Task<IResult> GetPlansAsync(IMaxioBillingClient maxio, CancellationToken cancellationToken)
    {
        try
        {
            var plans = await maxio.ListPlansAsync(cancellationToken);
            return Results.Ok(plans.Select(SubscriptionPlanResponse.From));
        }
        catch (MaxioApiException)
        {
            return Results.Problem("Subscription plans are temporarily unavailable.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<IResult> SubscribeAsync(
        SubscribeRequest request,
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        IMaxioBillingClient maxio,
        ILogger<SubscriptionEndpoints> logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.PlanHandle)] = new[] { "PlanHandle is required." } });
        }

        var shopper = await GetShopperAsync(context.User, userManager);
        if (shopper is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscription = await maxio.EnrollAsync(shopper, request.PlanHandle, cancellationToken);
            return Results.Created($"api/my-subscriptions/{subscription.Id}", SubscriptionResponse.From(subscription));
        }
        catch (UnknownPlanException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.PlanHandle)] = new[] { "PlanHandle is not an available subscription plan." } });
        }
        catch (MaxioApiException exception)
        {
            logger.LogWarning("Maxio enrollment rejected with status code {StatusCode} for eShop user {UserId}.", (int)exception.StatusCode, shopper.UserId);
            return Results.Problem("The subscription could not be created. Please try again.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<IResult> GetMySubscriptionsAsync(
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        IMaxioBillingClient maxio,
        CancellationToken cancellationToken)
    {
        var shopper = await GetShopperAsync(context.User, userManager);
        if (shopper is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscriptions = await maxio.ListSubscriptionsAsync(shopper, cancellationToken);
            return Results.Ok(subscriptions.Select(SubscriptionResponse.From));
        }
        catch (MaxioApiException)
        {
            return Results.Problem("Subscriptions are temporarily unavailable.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<MaxioShopper?> GetShopperAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user?.Email is null)
        {
            return null;
        }

        var displayName = user.Email.Split('@', 2)[0];
        return new MaxioShopper(user.Id, user.Email, displayName, "Customer");
    }
}

public sealed class SubscribeRequest
{
    [Required]
    public string PlanHandle { get; init; } = string.Empty;
}

public sealed record SubscriptionPlanResponse(string Handle, string Name, long PriceInCents, int Interval, string IntervalUnit)
{
    public static SubscriptionPlanResponse From(SubscriptionPlan plan) => new(plan.Handle, plan.Name, plan.PriceInCents, plan.Interval, plan.IntervalUnit);
}

public sealed record SubscriptionResponse(long Id, string PlanHandle, string PlanName, long PriceInCents, string State, DateTimeOffset? NextBillingAt)
{
    public static SubscriptionResponse From(SubscriptionSummary subscription) => new(subscription.Id, subscription.PlanHandle, subscription.PlanName, subscription.PriceInCents, subscription.State, subscription.NextBillingAt);
}
