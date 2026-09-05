using System;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>JWT-protected subscription plans and enrollment endpoints.</summary>
public sealed class SubscriptionEndpoints : IEndpoint<IResult>
{
    public const string JwtPolicyName = "SubscriptionJwt";

    // Routing is defined below; this member satisfies the endpoint package's discovery contract.
    public Task<IResult> HandleAsync() => Task.FromResult<IResult>(Results.NotFound());

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (SubscriptionEnrollmentService service, CancellationToken cancellationToken) =>
            await ExecutePlansAsync(() => service.GetPlansAsync(cancellationToken)))
            .RequireAuthorization(JwtPolicyName)
            .Produces<SubscriptionPlanDto[]>()
            .WithTags("Subscriptions");

        app.MapPost("api/subscriptions", async (SubscribeRequest request, ClaimsPrincipal principal,
            UserManager<ApplicationUser> userManager, SubscriptionEnrollmentService service, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.PlanHandle))
            {
                return Results.BadRequest(new { message = "planHandle is required." });
            }

            var user = await GetUserAsync(principal, userManager);
            return user is null
                ? Results.Unauthorized()
                : await ExecuteSubscriptionAsync(() => service.SubscribeAsync(user, request.PlanHandle, cancellationToken));
        })
        .RequireAuthorization(JwtPolicyName)
        .Accepts<SubscribeRequest>("application/json")
        .Produces<SubscriptionDto>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .WithTags("Subscriptions");

        app.MapGet("api/my-subscriptions", async (ClaimsPrincipal principal, UserManager<ApplicationUser> userManager,
            SubscriptionEnrollmentService service, CancellationToken cancellationToken) =>
        {
            var user = await GetUserAsync(principal, userManager);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            return await ExecuteMySubscriptionsAsync(() => service.GetMySubscriptionsAsync(user, cancellationToken));
        })
        .RequireAuthorization(JwtPolicyName)
        .Produces<MySubscriptionsResponse>()
        .Produces(StatusCodes.Status401Unauthorized)
        .WithTags("Subscriptions");
    }

    private static async Task<ApplicationUser?> GetUserAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var username = principal.Identity?.Name;
        return string.IsNullOrWhiteSpace(username) ? null : await userManager.FindByNameAsync(username);
    }

    private static async Task<IResult> ExecutePlansAsync(Func<Task<IReadOnlyList<SubscriptionPlanDto>>> action)
    {
        try { return Results.Ok(await action()); }
        catch (Exception exception) { return ToProblem(exception); }
    }

    private static async Task<IResult> ExecuteSubscriptionAsync(Func<Task<SubscriptionDto>> action)
    {
        try { return Results.Ok(await action()); }
        catch (Exception exception) { return ToProblem(exception); }
    }

    private static async Task<IResult> ExecuteMySubscriptionsAsync(Func<Task<IReadOnlyList<SubscriptionDto>>> action)
    {
        try { return Results.Ok(new MySubscriptionsResponse { Subscriptions = new List<SubscriptionDto>(await action()) }); }
        catch (Exception exception) { return ToProblem(exception); }
    }

    private static IResult ToProblem(Exception exception) => exception switch
    {
        SubscriptionRequestException request => Results.Problem(request.Message, statusCode: (int)request.StatusCode),
        MaxioConfigurationException configuration => Results.Problem(configuration.Message, statusCode: StatusCodes.Status503ServiceUnavailable),
        MaxioApiException api => Results.Problem("The billing service could not complete the request.", statusCode: (int)api.StatusCode),
        _ => Results.Problem("The subscription request could not be completed.", statusCode: StatusCodes.Status502BadGateway)
    };
}
