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
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionEndpoints : IEndpoint
{
    // The remote API's uniqueness token protects cross-process retries; this lock also makes ordinary double-clicks deterministic in one host.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscriptionLocks = new(StringComparer.Ordinal);

    public void AddRoute(IEndpointRouteBuilder app)
    {
        var authorization = new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme };

        app.MapGet("api/subscription-plans", async (IMaxioBillingClient maxio, CancellationToken cancellationToken) =>
            Results.Ok((await maxio.ListPlansAsync(cancellationToken)).Select(SubscriptionPlanResponse.From)))
            .RequireAuthorization(authorization)
            .Produces<IReadOnlyList<SubscriptionPlanResponse>>()
            .WithTags("Subscriptions");

        app.MapPost("api/subscriptions", async ([FromBody] CreateSubscriptionRequest request, ClaimsPrincipal principal,
            UserManager<ApplicationUser> userManager, IMaxioBillingClient maxio, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.PlanHandle)) return Results.BadRequest(new { error = "planHandle is required." });
            var user = await GetUserAsync(principal, userManager);
            if (user is null) return Results.Unauthorized();

            var key = $"{user.Id}:{request.PlanHandle}";
            var gate = SubscriptionLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);
            try
            {
                var plans = await maxio.ListPlansAsync(cancellationToken);
                var plan = plans.SingleOrDefault(plan => string.Equals(plan.Handle, request.PlanHandle, StringComparison.Ordinal));
                if (plan is null) return Results.BadRequest(new { error = "The requested plan is not available." });

                var customer = await maxio.EnsureCustomerAsync(CustomerReference(user.Id), user.Email!, CustomerFirstName(user), "Customer", cancellationToken);
                var subscriptionReference = SubscriptionReference(user.Id, plan.Handle);
                var existing = (await maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken))
                    .SingleOrDefault(subscription => string.Equals(subscription.Reference, subscriptionReference, StringComparison.Ordinal));
                var subscription = existing ?? await maxio.CreateSubscriptionAsync(customer.Id, plan.Handle, subscriptionReference, cancellationToken);
                return Results.Ok(MySubscriptionResponse.From(subscription));
            }
            finally
            {
                gate.Release();
                if (gate.CurrentCount == 1) SubscriptionLocks.TryRemove(new KeyValuePair<string, SemaphoreSlim>(key, gate));
            }
        })
        .RequireAuthorization(authorization)
        .Produces<MySubscriptionResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .WithTags("Subscriptions");

        app.MapGet("api/my-subscriptions", async (ClaimsPrincipal principal, UserManager<ApplicationUser> userManager,
            IMaxioBillingClient maxio, CancellationToken cancellationToken) =>
        {
            var user = await GetUserAsync(principal, userManager);
            if (user is null) return Results.Unauthorized();
            var customer = await maxio.FindCustomerAsync(CustomerReference(user.Id), cancellationToken);
            if (customer is null) return Results.Ok(Array.Empty<MySubscriptionResponse>());
            var subscriptions = await maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            return Results.Ok(subscriptions.Select(MySubscriptionResponse.From));
        })
        .RequireAuthorization(authorization)
        .Produces<IReadOnlyList<MySubscriptionResponse>>()
        .WithTags("Subscriptions");
    }

    private static Task<ApplicationUser?> GetUserAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager) =>
        userManager.FindByNameAsync(principal.Identity?.Name ?? string.Empty);

    private static string CustomerReference(string userId) => $"eshop-user:{userId}";
    private static string SubscriptionReference(string userId, string planHandle) => $"eshop-subscription:{userId}:{planHandle}";
    private static string CustomerFirstName(ApplicationUser user) => user.UserName?.Split('@')[0] ?? "eShop";
}

public sealed record CreateSubscriptionRequest(string PlanHandle);
public sealed record SubscriptionPlanResponse(string Handle, string Name, string? Description, decimal Price, int Interval, string IntervalUnit)
{
    public static SubscriptionPlanResponse From(MaxioPlan plan) => new(plan.Handle, plan.Name, plan.Description, plan.PriceInCents / 100m, plan.Interval, plan.IntervalUnit);
}

public sealed record MySubscriptionResponse(long Id, string PlanHandle, string PlanName, decimal Price, int Interval, string IntervalUnit, string State, DateTimeOffset? NextBillingAt)
{
    public static MySubscriptionResponse From(MaxioSubscription subscription) => new(subscription.Id, subscription.PlanHandle, subscription.PlanName,
        subscription.PriceInCents / 100m, subscription.Interval, subscription.IntervalUnit, subscription.State, subscription.NextBillingAt);
}
