using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
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

/// <summary>Public API endpoints for Maxio-backed recurring subscriptions.</summary>
public sealed class SubscriptionEndpoints : IEndpoint<IResult, CreateSubscriptionRequest>
{
    private static readonly AuthorizeAttribute JwtAuthorization = new()
    {
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
    };

    // The endpoint package discovers this class through IEndpoint; routing is deliberately
    // expressed below because the three routes have distinct request and dependency shapes.
    public Task<IResult> HandleAsync(CreateSubscriptionRequest request) =>
        throw new NotSupportedException("SubscriptionEndpoints routes requests through their mapped handlers.");

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (IMaxioAdvancedBillingClient maxio, CancellationToken cancellationToken) =>
            await ListPlansAsync(maxio, cancellationToken))
            .RequireAuthorization(JwtAuthorization)
            .Produces<SubscriptionPlansResponse>()
            .WithTags("Subscriptions");

        app.MapPost("api/subscriptions", async (CreateSubscriptionRequest request, ClaimsPrincipal principal, UserManager<ApplicationUser> userManager, IMaxioAdvancedBillingClient maxio, CancellationToken cancellationToken) =>
            await SubscribeAsync(request, principal, userManager, maxio, cancellationToken))
            .RequireAuthorization(JwtAuthorization)
            .Produces<SubscriptionResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("Subscriptions");

        app.MapGet("api/my-subscriptions", async (ClaimsPrincipal principal, UserManager<ApplicationUser> userManager, IMaxioAdvancedBillingClient maxio, CancellationToken cancellationToken) =>
            await ListMySubscriptionsAsync(principal, userManager, maxio, cancellationToken))
            .RequireAuthorization(JwtAuthorization)
            .Produces<MySubscriptionsResponse>()
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("Subscriptions");
    }

    private static async Task<IResult> SubscribeAsync(CreateSubscriptionRequest request, ClaimsPrincipal principal, UserManager<ApplicationUser> userManager, IMaxioAdvancedBillingClient maxio, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.PlanHandle)] = new[] { "PlanHandle is required." } });
        }

        var user = await GetUserAsync(principal, userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var plan = (await maxio.ListPlansAsync(cancellationToken)).SingleOrDefault(candidate =>
                string.Equals(candidate.Handle, request.PlanHandle, StringComparison.OrdinalIgnoreCase));
            if (plan is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.PlanHandle)] = new[] { "The selected plan is not available." } });
            }

            var customerReference = CustomerReference(user.Id);
            var customer = await maxio.FindCustomerByReferenceAsync(customerReference, cancellationToken)
                ?? await CreateCustomerSafelyAsync(maxio, user, customerReference, cancellationToken);

            var subscriptionReference = SubscriptionReference(user.Id, plan.Handle);
            var subscription = await maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken)
                ?? await maxio.CreateSubscriptionAsync(customer.Reference ?? customerReference, plan.Handle, subscriptionReference, cancellationToken);

            return Results.Created($"api/my-subscriptions/{subscription.Id}", ToResponse(subscription));
        }
        catch (MaxioIntegrationException)
        {
            return Results.Problem("The billing service is temporarily unavailable.", statusCode: StatusCodes.Status502BadGateway);
        }
        catch (HttpRequestException)
        {
            return Results.Problem("The billing service is temporarily unavailable.", statusCode: StatusCodes.Status502BadGateway);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Results.Problem("The billing service timed out. Please retry; your enrollment is protected against duplicates.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<IResult> ListMySubscriptionsAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager, IMaxioAdvancedBillingClient maxio, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(principal, userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var customer = await maxio.FindCustomerByReferenceAsync(CustomerReference(user.Id), cancellationToken);
            if (customer is null)
            {
                return Results.Ok(new MySubscriptionsResponse(Array.Empty<SubscriptionResponse>()));
            }

            var subscriptions = await maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            return Results.Ok(new MySubscriptionsResponse(subscriptions.Select(ToResponse)));
        }
        catch (MaxioIntegrationException)
        {
            return Results.Problem("The billing service is temporarily unavailable.", statusCode: StatusCodes.Status502BadGateway);
        }
        catch (HttpRequestException)
        {
            return Results.Problem("The billing service is temporarily unavailable.", statusCode: StatusCodes.Status502BadGateway);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Results.Problem("The billing service timed out. Please retry.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<IResult> ListPlansAsync(IMaxioAdvancedBillingClient maxio, CancellationToken cancellationToken)
    {
        try
        {
            var plans = await maxio.ListPlansAsync(cancellationToken);
            return Results.Ok(new SubscriptionPlansResponse(plans
                .Select(plan => new SubscriptionPlanResponse(plan.Handle, plan.Name, plan.PriceInCents, plan.Interval, plan.IntervalUnit))));
        }
        catch (MaxioIntegrationException)
        {
            return Results.Problem("The billing service is temporarily unavailable.", statusCode: StatusCodes.Status502BadGateway);
        }
        catch (HttpRequestException)
        {
            return Results.Problem("The billing service is temporarily unavailable.", statusCode: StatusCodes.Status502BadGateway);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Results.Problem("The billing service timed out. Please retry.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<MaxioCustomer> CreateCustomerSafelyAsync(IMaxioAdvancedBillingClient maxio, ApplicationUser user, string customerReference, CancellationToken cancellationToken)
    {
        var email = user.Email ?? user.UserName;
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new MaxioIntegrationException("The signed-in user does not have an email address.");
        }

        var username = user.UserName ?? email;
        var firstName = username.Split('@', 2)[0];
        firstName = firstName.Length > 50 ? firstName[..50] : firstName;
        try
        {
            return await maxio.CreateCustomerAsync(new MaxioCustomerInput(firstName, "Customer", email, customerReference), cancellationToken);
        }
        catch (MaxioIntegrationException exception) when (exception.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
        {
            // A concurrent request can win the unique-reference customer creation race.
            var existing = await maxio.FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            throw;
        }
    }

    private static Task<ApplicationUser?> GetUserAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var username = principal.FindFirstValue(ClaimTypes.Name);
        return string.IsNullOrWhiteSpace(username) ? Task.FromResult<ApplicationUser?>(null) : userManager.FindByNameAsync(username);
    }

    private static SubscriptionResponse ToResponse(MaxioSubscription subscription) => new(
        subscription.Id,
        subscription.Product?.Handle,
        subscription.Product?.Name,
        subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
        subscription.State,
        subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);

    private static string CustomerReference(string userId) => $"eshop-u-{StableReference(userId)}";
    private static string SubscriptionReference(string userId, string planHandle) => $"eshop-s-{StableReference(userId + ":" + planHandle)}";

    private static string StableReference(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..40];
}

public sealed class CreateSubscriptionRequest : BaseRequest
{
    public string PlanHandle { get; init; } = string.Empty;
}

public sealed record SubscriptionPlanResponse(string Handle, string Name, long? PriceInCents, int? Interval, string? IntervalUnit);
public sealed record SubscriptionPlansResponse(System.Collections.Generic.IEnumerable<SubscriptionPlanResponse> Plans);
public sealed record SubscriptionResponse(long Id, string? PlanHandle, string? PlanName, long? PriceInCents, string? State, DateTimeOffset? NextBillingDate);
public sealed record MySubscriptionsResponse(System.Collections.Generic.IEnumerable<SubscriptionResponse> Subscriptions);
