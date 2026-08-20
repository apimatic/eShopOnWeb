using System;
using System.Collections.Generic;
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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class SubscriptionEndpointRoutes
{
    public static IEndpointRouteBuilder MapSubscriptionEndpoints(this IEndpointRouteBuilder app)
    {
        var authorization = new AuthorizeAttribute
        {
            AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
        };

        app.MapGet("/api/subscription-plans", GetPlansAsync)
            .RequireAuthorization(authorization)
            .Produces<IReadOnlyList<SubscriptionPlanResponse>>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");

        app.MapPost("/api/subscriptions", SubscribeAsync)
            .RequireAuthorization(authorization)
            .Produces<SubscriptionResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");

        app.MapGet("/api/my-subscriptions", GetMySubscriptionsAsync)
            .RequireAuthorization(authorization)
            .Produces<IReadOnlyList<SubscriptionResponse>>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");

        return app;
    }

    private static async Task<IResult> GetPlansAsync(
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        var plans = await subscriptionService.GetPlansAsync(cancellationToken);
        return Results.Ok(plans.Select(SubscriptionPlanResponse.FromModel));
    }

    private static async Task<IResult> SubscribeAsync(
        SubscribeRequest request,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        var user = await GetAuthenticatedUserAsync(principal, userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var email = user.Email ?? user.UserName;
        if (string.IsNullOrWhiteSpace(email))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The authenticated account has no billing email.");
        }

        var subscription = await subscriptionService.SubscribeAsync(
            user.Id,
            user.UserName!,
            email,
            request.ProductHandle,
            cancellationToken);

        return Results.Created("/api/my-subscriptions", SubscriptionResponse.FromModel(subscription));
    }

    private static async Task<IResult> GetMySubscriptionsAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        var user = await GetAuthenticatedUserAsync(principal, userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var subscriptions = await subscriptionService.GetSubscriptionsAsync(user.Id, cancellationToken);
        return Results.Ok(subscriptions.Select(SubscriptionResponse.FromModel));
    }

    private static Task<ApplicationUser?> GetAuthenticatedUserAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager)
    {
        var userName = principal.Identity?.Name;
        return string.IsNullOrWhiteSpace(userName)
            ? Task.FromResult<ApplicationUser?>(null)
            : userManager.FindByNameAsync(userName);
    }
}

public sealed record SubscribeRequest(string ProductHandle);

public sealed record SubscriptionPlanResponse(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int? Interval,
    string? IntervalUnit)
{
    public static SubscriptionPlanResponse FromModel(SubscriptionPlan plan) =>
        new(plan.Handle, plan.Name, plan.Description, plan.PriceInCents, plan.Interval, plan.IntervalUnit);
}

public sealed record SubscriptionResponse(
    string Reference,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    string? Currency,
    string State,
    DateTimeOffset? NextBillingAt)
{
    public static SubscriptionResponse FromModel(SubscriptionDetails subscription) =>
        new(
            subscription.Reference,
            subscription.ProductHandle,
            subscription.ProductName,
            subscription.PriceInCents,
            subscription.Currency,
            subscription.State,
            subscription.NextBillingAt);
}
