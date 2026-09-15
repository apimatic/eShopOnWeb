using System;
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

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>JWT-protected shopper subscription endpoints backed by Maxio Advanced Billing.</summary>
public sealed class SubscriptionEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", GetPlansAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<SubscriptionPlanResponse[]>()
            .WithTags("SubscriptionEndpoints");

        app.MapPost("api/subscriptions", SubscribeAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<SubscriptionResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints");

        app.MapGet("api/my-subscriptions", GetMySubscriptionsAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<SubscriptionResponse[]>()
            .WithTags("SubscriptionEndpoints");
    }

    private static async Task<IResult> GetPlansAsync(ISubscriptionService subscriptions, CancellationToken cancellationToken) =>
        Results.Ok(await subscriptions.GetPlansAsync(cancellationToken));

    private static async Task<IResult> SubscribeAsync(CreateSubscriptionRequest request, HttpContext httpContext,
        UserManager<ApplicationUser> userManager, ISubscriptionService subscriptions, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { message = "planHandle is required." });
        }

        var user = await GetCurrentUserAsync(httpContext, userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var response = await subscriptions.SubscribeAsync(user, request.PlanHandle, cancellationToken);
            return Results.Created($"api/subscriptions/{response.Id}", response);
        }
        catch (SubscriptionValidationException exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
    }

    private static async Task<IResult> GetMySubscriptionsAsync(HttpContext httpContext, UserManager<ApplicationUser> userManager,
        ISubscriptionService subscriptions, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(httpContext, userManager);
        return user is null
            ? Results.Unauthorized()
            : Results.Ok(await subscriptions.GetMySubscriptionsAsync(user, cancellationToken));
    }

    private static Task<ApplicationUser?> GetCurrentUserAsync(HttpContext context, UserManager<ApplicationUser> userManager)
    {
        var userName = context.User.FindFirstValue(ClaimTypes.Name);
        return string.IsNullOrWhiteSpace(userName) ? Task.FromResult<ApplicationUser?>(null) : userManager.FindByNameAsync(userName);
    }
}
