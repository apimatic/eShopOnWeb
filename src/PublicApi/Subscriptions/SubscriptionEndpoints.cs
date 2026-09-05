using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Net.Http;
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

public sealed class SubscriptionPlansEndpoint : IEndpoint<IResult, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioSubscriptionService subscriptions, CancellationToken cancellationToken) =>
                await ExecuteAsync(() => subscriptions.ListPlansAsync(cancellationToken)))
            .Produces<IReadOnlyList<SubscriptionPlan>>()
            .WithTags("Subscriptions");
    }

    public Task<IResult> HandleAsync(IMaxioSubscriptionService subscriptions) =>
        ExecuteAsync(() => subscriptions.ListPlansAsync(CancellationToken.None));

    private static async Task<IResult> ExecuteAsync(Func<Task<IReadOnlyList<SubscriptionPlan>>> action)
    {
        try { return Results.Ok(await action()); }
        catch (MaxioApiException) { return Results.Problem("The billing service is currently unavailable.", statusCode: StatusCodes.Status502BadGateway); }
        catch (HttpRequestException) { return Results.Problem("The billing service is currently unavailable.", statusCode: StatusCodes.Status502BadGateway); }
    }
}

public sealed class CreateSubscriptionEndpoint : IEndpoint<IResult, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal principal, UserManager<ApplicationUser> userManager, IMaxioSubscriptionService subscriptions, CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.PlanHandle))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.PlanHandle)] = new[] { "PlanHandle is required." } });
                }

                var user = await CurrentUserAsync(principal, userManager);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                try
                {
                    return Results.Created($"api/my-subscriptions", await subscriptions.SubscribeAsync(user, request.PlanHandle, cancellationToken));
                }
                catch (SubscriptionPlanNotFoundException)
                {
                    return Results.NotFound();
                }
                catch (InvalidOperationException)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["user"] = new[] { "The signed-in user does not have a usable email address." } });
                }
                catch (MaxioApiException)
                {
                    return Results.Problem("The billing service is currently unavailable.", statusCode: StatusCodes.Status502BadGateway);
                }
                catch (HttpRequestException)
                {
                    return Results.Problem("The billing service is currently unavailable.", statusCode: StatusCodes.Status502BadGateway);
                }
            })
            .Accepts<SubscribeRequest>("application/json")
            .Produces<SubscriptionDetails>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("Subscriptions");
    }

    // The route needs the authenticated principal and UserManager in addition to the billing service.
    // MinimalApi.Endpoint requires this member for discovery; the route above is the request handler.
    public Task<IResult> HandleAsync(IMaxioSubscriptionService subscriptions) =>
        Task.FromResult(Results.Problem("This endpoint must be invoked through its HTTP route.", statusCode: StatusCodes.Status500InternalServerError));

    private static Task<ApplicationUser?> CurrentUserAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager) =>
        userManager.FindByNameAsync(principal.Identity?.Name ?? string.Empty);
}

public sealed class MySubscriptionsEndpoint : IEndpoint<IResult, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal principal, UserManager<ApplicationUser> userManager, IMaxioSubscriptionService subscriptions, CancellationToken cancellationToken) =>
            {
                var user = await userManager.FindByNameAsync(principal.Identity?.Name ?? string.Empty);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                try
                {
                    return Results.Ok(await subscriptions.GetSubscriptionsAsync(user, cancellationToken));
                }
                catch (MaxioApiException)
                {
                    return Results.Problem("The billing service is currently unavailable.", statusCode: StatusCodes.Status502BadGateway);
                }
                catch (HttpRequestException)
                {
                    return Results.Problem("The billing service is currently unavailable.", statusCode: StatusCodes.Status502BadGateway);
                }
            })
            .Produces<IReadOnlyList<SubscriptionDetails>>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("Subscriptions");
    }

    // See CreateSubscriptionEndpoint.HandleAsync for why the HTTP route owns the complete dependency set.
    public Task<IResult> HandleAsync(IMaxioSubscriptionService subscriptions) =>
        Task.FromResult(Results.Problem("This endpoint must be invoked through its HTTP route.", statusCode: StatusCodes.Status500InternalServerError));
}

public sealed class SubscribeRequest
{
    [Required]
    public string PlanHandle { get; init; } = string.Empty;
}
