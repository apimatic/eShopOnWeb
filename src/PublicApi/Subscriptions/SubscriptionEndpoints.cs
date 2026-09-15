using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>Lists the plans that can be subscribed to from the configured Maxio product family.</summary>
public sealed class SubscriptionPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (IMaxioBillingClient maxio, CancellationToken cancellationToken) =>
            await HandleAsync(maxio, cancellationToken))
            .RequireAuthorization()
            .Produces<IReadOnlyList<SubscriptionPlanResponse>>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("Subscriptions");
    }

    private static async Task<IResult> HandleAsync(IMaxioBillingClient maxio, CancellationToken cancellationToken)
    {
        try
        {
            var plans = await maxio.ListPlansAsync(cancellationToken);
            return Results.Ok(plans.Where(plan => plan.ArchivedAt is null).Select(SubscriptionEndpointMapping.ToPlanResponse));
        }
        catch (MaxioConfigurationException exception)
        {
            return SubscriptionEndpointMapping.ConfigurationProblem(exception);
        }
        catch (MaxioApiException)
        {
            return SubscriptionEndpointMapping.MaxioProblem();
        }
    }
}

/// <summary>Enrolls the current JWT user in a selected Maxio plan.</summary>
public sealed class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", async (
            CreateSubscriptionRequest request,
            HttpContext context,
            UserManager<ApplicationUser> userManager,
            IMaxioBillingClient maxio,
            UserSubscriptionLock userLock,
            CancellationToken cancellationToken) =>
            await HandleAsync(request, context, userManager, maxio, userLock, cancellationToken))
            .RequireAuthorization()
            .Produces<SubscriptionResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("Subscriptions");
    }

    private static async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        IMaxioBillingClient maxio,
        UserSubscriptionLock userLock,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
            return Results.Problem("A planHandle is required.", statusCode: StatusCodes.Status400BadRequest);

        var username = context.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(username))
            return Results.Unauthorized();

        var user = await userManager.FindByNameAsync(username);
        if (user is null)
            return Results.Unauthorized();

        var email = user.Email ?? user.UserName;
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@', StringComparison.Ordinal))
            return Results.Problem("The signed-in account needs a valid email address before it can subscribe.", statusCode: StatusCodes.Status400BadRequest);

        var (firstName, lastName) = GetCustomerName(user, email);
        using var enrollmentLock = await userLock.AcquireAsync(user.Id, cancellationToken);

        try
        {
            var plan = (await maxio.ListPlansAsync(cancellationToken)).SingleOrDefault(candidate =>
                candidate.ArchivedAt is null && string.Equals(candidate.Handle, request.PlanHandle, StringComparison.Ordinal));
            if (plan is null)
                return Results.NotFound();
            if (plan.RequireCreditCard)
                return Results.Problem("The selected plan requires a payment method, which this subscription endpoint does not collect.", statusCode: StatusCodes.Status400BadRequest);

            var customerReference = CustomerReference(user.Id);
            var customer = await maxio.FindOrCreateCustomerAsync(
                new MaxioCustomerCreate(firstName, lastName, email, customerReference), cancellationToken);

            var subscriptionReference = SubscriptionReference(user.Id, plan.Handle);
            var existing = (await maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken))
                .SingleOrDefault(subscription => string.Equals(subscription.Reference, subscriptionReference, StringComparison.Ordinal));
            if (existing is not null)
                return Results.Ok(SubscriptionEndpointMapping.ToSubscriptionResponse(existing));

            var created = await maxio.CreateSubscriptionAsync(
                new MaxioSubscriptionCreate(plan.Handle, customer.Id, subscriptionReference), cancellationToken);
            return Results.Created($"api/my-subscriptions/{created.Id}", SubscriptionEndpointMapping.ToSubscriptionResponse(created));
        }
        catch (MaxioConfigurationException exception)
        {
            return SubscriptionEndpointMapping.ConfigurationProblem(exception);
        }
        catch (MaxioApiException)
        {
            return SubscriptionEndpointMapping.MaxioProblem();
        }
    }

    internal static string CustomerReference(string userId) => $"eshop-web:{userId}";
    internal static string SubscriptionReference(string userId, string planHandle) => $"eshop-web:{userId}:{planHandle}";

    internal static (string FirstName, string LastName) GetCustomerName(ApplicationUser user, string email)
    {
        var source = (user.UserName ?? email).Split('@')[0]
            .Replace('.', ' ')
            .Replace('_', ' ')
            .Replace('-', ' ');
        var names = source.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return names.Length switch
        {
            >= 2 => (names[0], names[^1]),
            1 => (names[0], "Shopper"),
            _ => ("eShop", "Shopper")
        };
    }
}

/// <summary>Lists subscriptions owned by the current JWT user directly from Maxio.</summary>
public sealed class MySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", async (
            HttpContext context,
            UserManager<ApplicationUser> userManager,
            IMaxioBillingClient maxio,
            CancellationToken cancellationToken) =>
            await HandleAsync(context, userManager, maxio, cancellationToken))
            .RequireAuthorization()
            .Produces<IReadOnlyList<SubscriptionResponse>>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("Subscriptions");
    }

    private static async Task<IResult> HandleAsync(HttpContext context, UserManager<ApplicationUser> userManager, IMaxioBillingClient maxio, CancellationToken cancellationToken)
    {
        var username = context.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(username))
            return Results.Unauthorized();

        var user = await userManager.FindByNameAsync(username);
        if (user is null)
            return Results.Unauthorized();

        var email = user.Email ?? user.UserName;
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@', StringComparison.Ordinal))
            return Results.Problem("The signed-in account needs a valid email address before it can subscribe.", statusCode: StatusCodes.Status400BadRequest);

        try
        {
            var name = CreateSubscriptionEndpoint.GetCustomerName(user, email);
            var customer = await maxio.FindOrCreateCustomerAsync(
                new MaxioCustomerCreate(name.FirstName, name.LastName, email,
                    CreateSubscriptionEndpoint.CustomerReference(user.Id)), cancellationToken);
            var subscriptions = await maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            return Results.Ok(subscriptions.Select(SubscriptionEndpointMapping.ToSubscriptionResponse));
        }
        catch (MaxioConfigurationException exception)
        {
            return SubscriptionEndpointMapping.ConfigurationProblem(exception);
        }
        catch (MaxioApiException)
        {
            return SubscriptionEndpointMapping.MaxioProblem();
        }
    }
}

public sealed class CreateSubscriptionRequest
{
    public string PlanHandle { get; init; } = string.Empty;
}

public sealed record SubscriptionPlanResponse(string Handle, string Name, long PriceInCents, int Interval, string IntervalUnit, bool RequiresPaymentMethod);
public sealed record SubscriptionResponse(long Id, string State, SubscriptionPlanResponse? Plan, long PriceInCents, DateTimeOffset? NextBillingAt, DateTimeOffset? NextAssessmentAt);

internal static class SubscriptionEndpointMapping
{
    public static SubscriptionPlanResponse ToPlanResponse(MaxioPlan plan) =>
        new(plan.Handle, plan.Name, plan.PriceInCents, plan.Interval, plan.IntervalUnit, plan.RequireCreditCard);

    public static SubscriptionResponse ToSubscriptionResponse(MaxioSubscription subscription) =>
        new(subscription.Id, subscription.State,
            subscription.Plan is null ? null : ToPlanResponse(subscription.Plan),
            subscription.ProductPriceInCents, subscription.CurrentPeriodEndsAt, subscription.NextAssessmentAt);

    public static IResult ConfigurationProblem(MaxioConfigurationException exception) =>
        Results.Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable, title: "Subscription billing is not configured.");

    public static IResult MaxioProblem() =>
        Results.Problem("The billing provider could not complete the request. Please retry.", statusCode: StatusCodes.Status502BadGateway, title: "Subscription billing is temporarily unavailable.");
}
