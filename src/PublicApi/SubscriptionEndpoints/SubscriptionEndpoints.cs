using System;
using System.Linq;
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
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Lists Maxio plans and manages the current shopper's subscriptions.</summary>
public sealed class SubscriptionEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SubscriptionEnrollmentService enrollmentService, CancellationToken cancellationToken) =>
            {
                try
                {
                    var plans = await enrollmentService.GetPlansAsync(cancellationToken);
                    return Results.Ok(new SubscriptionPlansResponse(plans.Select(SubscriptionPlanResponse.FromProduct).ToArray()));
                }
                catch (MaxioApiException)
                {
                    return BillingUnavailable();
                }
                catch (HttpRequestException)
                {
                    return BillingUnavailable();
                }
            })
            .Produces<SubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");

        app.MapPost("api/subscriptions", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SubscribeRequest request, ClaimsPrincipal principal, UserManager<ApplicationUser> userManager, SubscriptionEnrollmentService enrollmentService, CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.PlanHandle))
                {
                    return Results.BadRequest(new { message = "planHandle is required." });
                }

                var user = await FindCurrentUserAsync(principal, userManager);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                try
                {
                    var enrollment = await enrollmentService.EnrollAsync(user, request.PlanHandle, cancellationToken);
                    var response = SubscriptionResponse.From(enrollment.Plan, enrollment.Subscription, enrollment.AlreadySubscribed);
                    return enrollment.AlreadySubscribed ? Results.Ok(response) : Results.Created($"api/subscriptions/{response.Id}", response);
                }
                catch (SubscriptionValidationException exception)
                {
                    return Results.BadRequest(new { message = exception.Message });
                }
                catch (MaxioApiException exception) when (exception.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
                {
                    return Results.BadRequest(new { message = "Maxio could not create this subscription. Check the plan's billing requirements." });
                }
                catch (MaxioApiException)
                {
                    return BillingUnavailable();
                }
                catch (HttpRequestException)
                {
                    return BillingUnavailable();
                }
            })
            .Produces<SubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<SubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");

        app.MapGet("api/my-subscriptions", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal principal, UserManager<ApplicationUser> userManager, SubscriptionEnrollmentService enrollmentService, CancellationToken cancellationToken) =>
            {
                var user = await FindCurrentUserAsync(principal, userManager);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                try
                {
                    var subscriptions = await enrollmentService.GetMySubscriptionsAsync(user, cancellationToken);
                    return Results.Ok(new MySubscriptionsResponse(subscriptions.Select(SubscriptionResponse.FromSubscription).ToArray()));
                }
                catch (MaxioApiException)
                {
                    return BillingUnavailable();
                }
                catch (HttpRequestException)
                {
                    return BillingUnavailable();
                }
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    private static Task<ApplicationUser?> FindCurrentUserAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
        => string.IsNullOrWhiteSpace(principal.Identity?.Name)
            ? Task.FromResult<ApplicationUser?>(null)
            : userManager.FindByNameAsync(principal.Identity.Name);

    private static IResult BillingUnavailable() => Results.Problem(
        title: "Billing service unavailable",
        detail: "Maxio Advanced Billing could not complete the request. Please retry.",
        statusCode: StatusCodes.Status502BadGateway);
}

public sealed class SubscribeRequest { public string? PlanHandle { get; init; } }
public sealed record SubscriptionPlansResponse(SubscriptionPlanResponse[] Plans);
public sealed record MySubscriptionsResponse(SubscriptionResponse[] Subscriptions);
public sealed record SubscriptionPlanResponse(string Handle, string Name, string? Description, decimal Price, int Interval, string IntervalUnit)
{
    public static SubscriptionPlanResponse FromProduct(MaxioProduct product) => new(product.Handle!, product.Name, product.Description, product.PriceInCents / 100m, product.Interval, product.IntervalUnit);
}

public sealed record SubscriptionResponse(long Id, string PlanHandle, string PlanName, decimal Price, string State, DateTimeOffset? NextBillingAt, bool AlreadySubscribed)
{
    public static SubscriptionResponse From(MaxioProduct plan, MaxioSubscription subscription, bool alreadySubscribed)
        => new(subscription.Id, plan.Handle!, plan.Name, subscription.ProductPriceInCents / 100m, subscription.State, subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt, alreadySubscribed);

    public static SubscriptionResponse FromSubscription(MaxioSubscription subscription)
        => new(subscription.Id, subscription.Product?.Handle ?? string.Empty, subscription.Product?.Name ?? "Subscription", subscription.ProductPriceInCents / 100m, subscription.State, subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt, false);
}
