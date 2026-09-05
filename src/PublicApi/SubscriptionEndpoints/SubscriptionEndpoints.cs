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
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>JWT-authenticated Maxio subscription enrollment and account endpoints.</summary>
public sealed class SubscriptionEndpoints : IEndpoint<IResult, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioSubscriptionService subscriptions, CancellationToken cancellationToken) =>
            {
                try
                {
                    var response = new ListSubscriptionPlansResponse();
                    response.Plans.AddRange(await subscriptions.GetPlansAsync(cancellationToken));
                    return Results.Ok(response);
                }
                catch (MaxioApiException)
                {
                    return MaxioUnavailable();
                }
                catch (OptionsValidationException)
                {
                    return MaxioNotConfigured();
                }
            })
            .Produces<ListSubscriptionPlansResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");

        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal principal, UserManager<ApplicationUser> userManager,
                IMaxioSubscriptionService subscriptions, CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.PlanHandle))
                {
                    return Results.ValidationProblem(new System.Collections.Generic.Dictionary<string, string[]>
                    {
                        [nameof(request.PlanHandle)] = new[] { "PlanHandle is required." }
                    });
                }

                var user = await FindCallerAsync(principal, userManager);
                if (user is null || string.IsNullOrWhiteSpace(user.Email))
                {
                    return Results.Unauthorized();
                }

                try
                {
                    var existing = await subscriptions.FindSubscriptionAsync(user.Id, request.PlanHandle, cancellationToken);
                    if (existing is not null)
                    {
                        return Results.Ok(new CreateSubscriptionResponse { Subscription = existing, AlreadySubscribed = true });
                    }

                    var subscription = await subscriptions.SubscribeAsync(user.Id, user.Email, request.PlanHandle, cancellationToken);
                    return Results.Created($"api/subscriptions/{subscription.Id}",
                        new CreateSubscriptionResponse { Subscription = subscription, AlreadySubscribed = false });
                }
                catch (ArgumentException exception) when (exception.ParamName == "planHandle")
                {
                    return Results.ValidationProblem(new System.Collections.Generic.Dictionary<string, string[]>
                    {
                        [nameof(request.PlanHandle)] = new[] { exception.Message }
                    });
                }
                catch (MaxioApiException)
                {
                    return MaxioUnavailable();
                }
                catch (OptionsValidationException)
                {
                    return MaxioNotConfigured();
                }
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");

        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal principal, UserManager<ApplicationUser> userManager,
                IMaxioSubscriptionService subscriptions, CancellationToken cancellationToken) =>
            {
                var user = await FindCallerAsync(principal, userManager);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                try
                {
                    var response = new ListMySubscriptionsResponse();
                    response.Subscriptions.AddRange(await subscriptions.GetSubscriptionsAsync(user.Id, cancellationToken));
                    return Results.Ok(response);
                }
                catch (MaxioApiException)
                {
                    return MaxioUnavailable();
                }
                catch (OptionsValidationException)
                {
                    return MaxioNotConfigured();
                }
            })
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    // The MinimalApi.Endpoint discovery contract requires a handler in addition to
    // AddRoute. This endpoint group exposes three route-specific handlers above.
    public Task<IResult> HandleAsync(IMaxioSubscriptionService subscriptions) =>
        Task.FromResult<IResult>(Results.NotFound());

    private static async Task<ApplicationUser?> FindCallerAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var userName = principal.Identity?.Name;
        return string.IsNullOrWhiteSpace(userName) ? null : await userManager.FindByNameAsync(userName);
    }

    private static IResult MaxioUnavailable() => Results.Problem(
        title: "Subscription service is temporarily unavailable.",
        statusCode: StatusCodes.Status502BadGateway);

    private static IResult MaxioNotConfigured() => Results.Problem(
        title: "Subscription billing is not configured.",
        statusCode: StatusCodes.Status503ServiceUnavailable);
}
