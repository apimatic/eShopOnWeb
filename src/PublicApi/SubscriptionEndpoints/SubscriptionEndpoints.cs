using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", GetPlansAsync)
            .RequireAuthorization()
            .Produces<SubscriptionPlanDto[]>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");

        app.MapPost("api/subscriptions", SubscribeAsync)
            .RequireAuthorization()
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("SubscriptionEndpoints");

        app.MapGet("api/my-subscriptions", GetMySubscriptionsAsync)
            .RequireAuthorization()
            .Produces<SubscriptionDto[]>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    private static async Task<IResult> GetPlansAsync(
        ISubscriptionService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.GetPlansAsync(cancellationToken));

    private static async Task<IResult> SubscribeAsync(
        SubscribeRequest? request,
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        ISubscriptionService service,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.BadRequest(new { error = "productHandle is required." });
        }

        var user = await ResolveUserAsync(context, userManager);
        if (user is null) return Results.Unauthorized();

        var response = await service.SubscribeAsync(user, request.ProductHandle, cancellationToken);
        if (response is null) return Results.NotFound(new { error = "Subscription plan not found." });

        return response.Created
            ? Results.Created("/api/my-subscriptions", response)
            : Results.Ok(response);
    }

    private static async Task<IResult> GetMySubscriptionsAsync(
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        ISubscriptionService service,
        CancellationToken cancellationToken)
    {
        var user = await ResolveUserAsync(context, userManager);
        if (user is null) return Results.Unauthorized();
        return Results.Ok(await service.GetSubscriptionsAsync(user, cancellationToken));
    }

    private static async Task<SubscriptionUser?> ResolveUserAsync(
        HttpContext context,
        UserManager<ApplicationUser> userManager)
    {
        var userName = context.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName)) return null;

        var user = await userManager.FindByNameAsync(userName);
        if (user is null) return null;

        var email = string.IsNullOrWhiteSpace(user.Email) ? userName : user.Email;
        return new SubscriptionUser(user.Id, userName, email);
    }
}
