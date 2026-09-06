using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// JWT-authenticated subscription catalog and enrollment endpoints backed by Maxio Advanced Billing.
/// </summary>
public sealed class SubscriptionEndpoints : IEndpoint<IResult, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (IMaxioSubscriptionService subscriptions, CancellationToken cancellationToken) =>
            {
                try
                {
                    var plans = await subscriptions.GetPlansAsync(cancellationToken);
                    return Results.Ok(new SubscriptionPlansResponse(plans.Select(SubscriptionPlanResponse.From).ToList()));
                }
                catch (MaxioApiException)
                {
                    return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Subscription plans are temporarily unavailable.");
                }
            })
            .Produces<SubscriptionPlansResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");

        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (CreateSubscriptionRequest request, HttpContext context, IMaxioSubscriptionService subscriptions, CancellationToken cancellationToken) =>
            {
                try
                {
                    var result = await subscriptions.EnrollAsync(context.User, request.PlanHandle, cancellationToken);
                    var response = new SubscriptionEnrollmentResponse(SubscriptionResponse.From(result.Subscription), result.Created);
                    return result.Created
                        ? Results.Created($"api/my-subscriptions/{result.Subscription.Id}", response)
                        : Results.Ok(response);
                }
                catch (ArgumentException exception)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["planHandle"] = new[] { exception.Message } });
                }
                catch (SubscriptionInProgressException exception)
                {
                    return Results.Conflict(new ProblemDetails { Title = "Subscription enrollment in progress", Detail = exception.Message, Status = StatusCodes.Status409Conflict });
                }
                catch (MaxioApiException)
                {
                    return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Maxio could not enroll this subscription.");
                }
                catch (UnauthorizedAccessException)
                {
                    return Results.Unauthorized();
                }
            })
            .Accepts<CreateSubscriptionRequest>("application/json")
            .Produces<SubscriptionEnrollmentResponse>(StatusCodes.Status201Created)
            .Produces<SubscriptionEnrollmentResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");

        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (HttpContext context, IMaxioSubscriptionService subscriptions, CancellationToken cancellationToken) =>
            {
                try
                {
                    var results = await subscriptions.GetMySubscriptionsAsync(context.User, cancellationToken);
                    return Results.Ok(new MySubscriptionsResponse(results.Select(SubscriptionResponse.From).ToList()));
                }
                catch (MaxioApiException)
                {
                    return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Subscriptions are temporarily unavailable.");
                }
                catch (UnauthorizedAccessException)
                {
                    return Results.Unauthorized();
                }
            })
            .Produces<MySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    // Required by the project's MinimalApi.Endpoint convention. Routes above carry the
    // individual HTTP contracts because this feature exposes three related operations.
    public async Task<IResult> HandleAsync(IMaxioSubscriptionService subscriptions)
    {
        var plans = await subscriptions.GetPlansAsync(CancellationToken.None);
        return Results.Ok(new SubscriptionPlansResponse(plans.Select(SubscriptionPlanResponse.From).ToList()));
    }
}

public sealed class CreateSubscriptionRequest
{
    public string PlanHandle { get; init; } = string.Empty;
}

public sealed record SubscriptionPlansResponse(IReadOnlyList<SubscriptionPlanResponse> Plans);
public sealed record SubscriptionPlanResponse(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    bool RequiresPaymentMethod,
    string? Currency)
{
    public static SubscriptionPlanResponse From(MaxioPlan plan) => new(
        plan.Handle, plan.Name, plan.Description, plan.PriceInCents, plan.Interval,
        plan.IntervalUnit, plan.RequiresPaymentMethod, plan.Currency);
}

public sealed record SubscriptionEnrollmentResponse(SubscriptionResponse Subscription, bool Created);
public sealed record MySubscriptionsResponse(IReadOnlyList<SubscriptionResponse> Subscriptions);
public sealed record SubscriptionResponse(
    int Id,
    string State,
    string PlanHandle,
    string PlanName,
    long PriceInCents,
    string? Currency,
    DateTimeOffset? NextBillingAt)
{
    public static SubscriptionResponse From(MaxioSubscription subscription) => new(
        subscription.Id, subscription.State, subscription.PlanHandle, subscription.PlanName,
        subscription.PriceInCents, subscription.Currency, subscription.NextBillingAt);
}
