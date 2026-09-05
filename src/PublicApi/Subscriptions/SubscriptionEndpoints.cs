using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>Maxio Advanced Billing subscription endpoints for authenticated shoppers.</summary>
public sealed class SubscriptionEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (ISubscriptionService subscriptions, CancellationToken cancellationToken) =>
            {
                var plans = await subscriptions.GetPlansAsync(cancellationToken);
                return Results.Ok(new SubscriptionPlansResponse(plans.Select(SubscriptionPlanDto.From).ToList()));
            })
            .Produces<SubscriptionPlansResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("Subscriptions");

        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (SubscribeRequest request, HttpContext context, ISubscriptionService subscriptions, CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.ProductHandle))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["productHandle"] = new[] { "A product handle is required." } });

                var result = await subscriptions.SubscribeAsync(context.User.Identity!.Name!, request.ProductHandle, cancellationToken);
                var response = new SubscribeResponse(SubscriptionDto.From(result.Subscription), result.Created);
                return result.Created
                    ? Results.Created($"api/my-subscriptions/{result.Subscription.Id}", response)
                    : Results.Ok(response);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("Subscriptions");

        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (HttpContext context, ISubscriptionService subscriptions, CancellationToken cancellationToken) =>
            {
                var items = await subscriptions.GetSubscriptionsAsync(context.User.Identity!.Name!, cancellationToken);
                return Results.Ok(new MySubscriptionsResponse(items.Select(SubscriptionDto.From).ToList()));
            })
            .Produces<MySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("Subscriptions");
    }
}

public sealed record SubscribeRequest(string? ProductHandle);
public sealed record SubscriptionPlansResponse(IReadOnlyList<SubscriptionPlanDto> SubscriptionPlans);
public sealed record MySubscriptionsResponse(IReadOnlyList<SubscriptionDto> Subscriptions);
public sealed record SubscribeResponse(SubscriptionDto Subscription, bool Created);
public sealed record SubscriptionPlanDto(string Handle, string Name, string? Description, long PriceInCents, int Interval, string IntervalUnit)
{
    public static SubscriptionPlanDto From(MaxioPlan plan) => new(plan.Handle, plan.Name, plan.Description, plan.PriceInCents, plan.Interval, plan.IntervalUnit);
}
public sealed record SubscriptionDto(long Id, string State, string? ProductHandle, string? ProductName, long ProductPriceInCents, DateTimeOffset? NextBillingAt)
{
    public static SubscriptionDto From(MaxioSubscription subscription) => new(
        subscription.Id,
        subscription.State,
        subscription.ProductHandle,
        subscription.ProductName,
        subscription.ProductPriceInCents,
        subscription.NextAssessmentAt);
}
