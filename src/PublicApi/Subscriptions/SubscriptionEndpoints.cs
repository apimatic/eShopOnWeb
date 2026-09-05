using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>JWT-protected subscription plans and enrollment endpoints.</summary>
public sealed class SubscriptionEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        var plans = app.MapGet("api/subscription-plans", GetPlansAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<SubscriptionPlansResponse>()
            .WithTags("Subscriptions");

        var subscribe = app.MapPost("api/subscriptions", SubscribeAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<SubscriptionResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("Subscriptions");

        var mine = app.MapGet("api/my-subscriptions", GetMySubscriptionsAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<MySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("Subscriptions");
    }

    private static async Task<IResult> GetPlansAsync(SubscriptionService subscriptions, CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await subscriptions.GetPlansAsync(cancellationToken));
        }
        catch (MaxioApiException)
        {
            return BillingUnavailable();
        }
    }

    private static async Task<IResult> SubscribeAsync(
        SubscribeRequest request,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager,
        SubscriptionService subscriptions,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.Problem("A planHandle is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var result = await subscriptions.SubscribeAsync(user, request.PlanHandle, cancellationToken);
            return Results.Created($"api/my-subscriptions/{result.Id}", result);
        }
        catch (UnknownPlanException)
        {
            return Results.Problem("The requested plan is not available.", statusCode: StatusCodes.Status400BadRequest);
        }
        catch (MaxioApiException)
        {
            return BillingUnavailable();
        }
    }

    private static async Task<IResult> GetMySubscriptionsAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager,
        SubscriptionService subscriptions,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            return Results.Ok(await subscriptions.GetMySubscriptionsAsync(user, cancellationToken));
        }
        catch (MaxioApiException)
        {
            return BillingUnavailable();
        }
    }

    private static IResult BillingUnavailable() => Results.Problem("Subscription billing is temporarily unavailable.", statusCode: StatusCodes.Status502BadGateway);
}

public sealed class SubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EnrollmentLocks = new();
    private readonly IMaxioClient _maxio;
    private readonly MaxioOptions _options;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(IMaxioClient maxio, IOptions<MaxioOptions> options, ILogger<SubscriptionService> logger)
    {
        _maxio = maxio;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SubscriptionPlansResponse> GetPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxio.ListProductsAsync(_options.ProductFamilyHandle, cancellationToken);
        return new SubscriptionPlansResponse(products
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(MapPlan)
            .OrderBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray());
    }

    public async Task<SubscriptionResponse> SubscribeAsync(ApplicationUser user, string planHandle, CancellationToken cancellationToken)
    {
        var gate = EnrollmentLocks.GetOrAdd(user.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var plans = await _maxio.ListProductsAsync(_options.ProductFamilyHandle, cancellationToken);
            var plan = plans.FirstOrDefault(product => product.ArchivedAt is null && string.Equals(product.Handle, planHandle, StringComparison.Ordinal));
            if (plan is null || string.IsNullOrWhiteSpace(plan.Handle)) throw new UnknownPlanException();

            var subscriptionReference = SubscriptionReference(user.Id, plan.Handle);
            var enrollment = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (enrollment is not null)
            {
                return MapSubscription(enrollment);
            }

            var customer = await EnsureCustomerAsync(user, cancellationToken);
            var existing = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var matching = existing.FirstOrDefault(subscription =>
                string.Equals(subscription.Product?.Handle, plan.Handle, StringComparison.Ordinal) && IsCurrent(subscription.State));

            if (matching is not null)
            {
                return MapSubscription(matching);
            }

            // The deterministic reference is resolved by the Maxio find-subscription operation.
            // It protects retries after a timeout and, together with the local gate, repeat submits.
            try
            {
                var created = await _maxio.CreateSubscriptionAsync(plan.Handle, customer.Id, subscriptionReference, cancellationToken);
                return MapSubscription(created);
            }
            catch (MaxioApiException exception) when (exception.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
            {
                enrollment = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
                if (enrollment is not null) return MapSubscription(enrollment);
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<MySubscriptionsResponse> GetMySubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var customer = await _maxio.FindCustomerByReferenceAsync(CustomerReference(user.Id), cancellationToken);
        if (customer is null) return new MySubscriptionsResponse(Array.Empty<SubscriptionResponse>());

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return new MySubscriptionsResponse(subscriptions
            .Select(MapSubscription)
            .OrderByDescending(subscription => subscription.NextBillingAt)
            .ToArray());
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var reference = CustomerReference(user.Id);
        var customer = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is not null) return customer;

        var email = user.Email ?? user.UserName;
        if (string.IsNullOrWhiteSpace(email)) throw new MaxioApiException(System.Net.HttpStatusCode.UnprocessableEntity, "The authenticated user has no email address.");

        var localPart = email.Split('@', 2)[0];
        var firstName = string.IsNullOrWhiteSpace(localPart) ? "eShop" : localPart;
        try
        {
            return await _maxio.CreateCustomerAsync(new MaxioCreateCustomer(firstName, "Shopper", email, reference), cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
        {
            // Customer references are unique in Maxio. A concurrent create can lose the race; read the winner.
            customer = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (customer is not null) return customer;
            _logger.LogWarning("Maxio rejected creation of customer reference {CustomerReference}.", reference);
            throw;
        }
    }

    private static bool IsCurrent(string state) => !string.Equals(state, "canceled", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(state, "expired", StringComparison.OrdinalIgnoreCase);

    private static string CustomerReference(string userId) => $"eshop-user-{userId}";
    private static string SubscriptionReference(string userId, string planHandle) => $"eshop-subscription-{userId}-{planHandle}";
    private static SubscriptionPlan MapPlan(MaxioProduct product) => new(product.Handle!, product.Name, product.Description, product.PriceInCents, product.Interval, product.IntervalUnit);
    private static SubscriptionResponse MapSubscription(MaxioSubscription subscription) => new(
        subscription.Id,
        subscription.Product?.Handle ?? string.Empty,
        subscription.Product?.Name ?? string.Empty,
        subscription.ProductPriceInCents,
        subscription.State,
        subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);
}

public sealed class UnknownPlanException : Exception;
public sealed record SubscribeRequest(string? PlanHandle);
public sealed record SubscriptionPlan(string Handle, string Name, string? Description, long PriceInCents, int Interval, string IntervalUnit);
public sealed record SubscriptionPlansResponse(IReadOnlyList<SubscriptionPlan> Plans);
public sealed record SubscriptionResponse(long Id, string PlanHandle, string PlanName, long PriceInCents, string State, DateTimeOffset? NextBillingAt);
public sealed record MySubscriptionsResponse(IReadOnlyList<SubscriptionResponse> Subscriptions);
