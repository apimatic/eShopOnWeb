using System.Collections.Generic;
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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed record ListSubscriptionPlansResponse(IReadOnlyList<SubscriptionPlanDto> Plans);
public sealed record ListMySubscriptionsResponse(IReadOnlyList<SubscriptionDto> Subscriptions);

public sealed class CreateSubscriptionRequest
{
    public string ProductHandle { get; init; } = string.Empty;
}

public sealed class ListSubscriptionPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (ISubscriptionService service, CancellationToken cancellationToken) =>
                    await HandleAsync(service, cancellationToken))
            .Produces<ListSubscriptionPlansResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public static async Task<IResult> HandleAsync(
        ISubscriptionService service,
        CancellationToken cancellationToken)
    {
        var plans = await service.GetPlansAsync(cancellationToken);
        return Results.Ok(new ListSubscriptionPlansResponse(plans));
    }
}

public sealed class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (CreateSubscriptionRequest request,
                    ClaimsPrincipal principal,
                    UserManager<ApplicationUser> userManager,
                    ISubscriptionService service,
                    CancellationToken cancellationToken) =>
                    await HandleAsync(request, principal, userManager, service, cancellationToken))
            .Produces<SubscriptionDto>()
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .WithTags("SubscriptionEndpoints");
    }

    public static async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager,
        ISubscriptionService service,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.ProductHandle)] = ["A product handle is required."]
            });
        }

        var user = await CurrentBillingUser.GetAsync(principal, userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var result = await service.SubscribeAsync(user, request.ProductHandle.Trim(), cancellationToken);
        return result.Created
            ? Results.Created("/api/my-subscriptions", result.Subscription)
            : Results.Ok(result.Subscription);
    }
}

public sealed class ListMySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (ClaimsPrincipal principal,
                    UserManager<ApplicationUser> userManager,
                    ISubscriptionService service,
                    CancellationToken cancellationToken) =>
                    await HandleAsync(principal, userManager, service, cancellationToken))
            .Produces<ListMySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public static async Task<IResult> HandleAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager,
        ISubscriptionService service,
        CancellationToken cancellationToken)
    {
        var user = await CurrentBillingUser.GetAsync(principal, userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var subscriptions = await service.GetSubscriptionsAsync(user, cancellationToken);
        return Results.Ok(new ListMySubscriptionsResponse(subscriptions));
    }
}

internal static class CurrentBillingUser
{
    public static async Task<BillingUser?> GetAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user is null || string.IsNullOrWhiteSpace(user.Email))
        {
            return null;
        }

        return new BillingUser(user.Id, user.Email, user.UserName ?? user.Email);
    }
}
