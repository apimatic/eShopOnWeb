using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Subscription states from which a subscription is considered a live enrollment
/// (i.e. it satisfies "the user already has this plan"). Everything else
/// (canceled, expired, failed_to_create, trial_ended) allows a fresh signup.
/// </summary>
internal static class LiveSubscriptionStates
{
    public static readonly IReadOnlyCollection<string> Values = new[]
    {
        "pending", "awaiting_signup", "trialing", "assessing", "active",
        "soft_failure", "past_due", "unpaid", "suspended", "on_hold"
    };
}

/// <summary>
/// Application-level orchestration of the Maxio billing flows:
/// idempotently ensuring a Maxio customer exists for an eShopOnWeb user,
/// enrolling them in a plan, and reading their enrollments back.
/// </summary>
public interface ISubscriptionService
{
    Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken = default);
    Task<SubscribeResult> SubscribeAsync(ClaimsPrincipal principal, string productHandle, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MaxioSubscription>> ListMySubscriptionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);
}

/// <summary>
/// Outcome of an enrollment attempt.
/// </summary>
public class SubscribeResult
{
    public SubscribeResult(MaxioSubscription subscription, bool alreadySubscribed)
    {
        Subscription = subscription;
        AlreadySubscribed = alreadySubscribed;
    }

    public MaxioSubscription Subscription { get; }
    public bool AlreadySubscribed { get; }
}

public class SubscriptionService : ISubscriptionService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMaxioBillingClient _billingClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<SubscriptionService> _logger;

    // Serializes concurrent enroll attempts for the same user + plan, so a
    // double-click cannot create two Maxio subscriptions.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EnrollLocks = new();

    public SubscriptionService(
        UserManager<ApplicationUser> userManager,
        IMaxioBillingClient billingClient,
        IOptions<MaxioOptions> options,
        ILogger<SubscriptionService> logger)
    {
        _userManager = userManager;
        _billingClient = billingClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        return await _billingClient.ListProductsInFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
    }

    public async Task<SubscribeResult> SubscribeAsync(ClaimsPrincipal principal, string productHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new ArgumentException("A product handle is required.", nameof(productHandle));
        }

        var user = await ResolveUserAsync(principal);

        var plans = await _billingClient.ListProductsInFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
        var plan = plans.FirstOrDefault(p =>
            string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase));

        if (plan is null)
        {
            throw new UnknownSubscriptionPlanException(productHandle);
        }

        var enrollLock = EnrollLocks.GetOrAdd($"{user.Id}:{plan.Handle}", _ => new SemaphoreSlim(1, 1));
        await enrollLock.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureMaxioCustomerAsync(user, cancellationToken);

            var existing = await FindLiveSubscriptionAsync(customer.Id, plan.Handle!, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "User {UserId} already holds subscription {SubscriptionId} for plan {PlanHandle}; returning it.",
                    user.Id, existing.Id, plan.Handle);
                return new SubscribeResult(existing, alreadySubscribed: true);
            }

            _logger.LogInformation(
                "Enrolling user {UserId} (Maxio customer {CustomerId}) in plan {PlanHandle}.",
                user.Id, customer.Id, plan.Handle);

            var created = await _billingClient.CreateSubscriptionAsync(plan.Handle!, customer.Id, cancellationToken);
            return new SubscribeResult(created, alreadySubscribed: false);
        }
        finally
        {
            enrollLock.Release();
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListMySubscriptionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(principal);
        var customer = await EnsureMaxioCustomerAsync(user, cancellationToken);

        var subscriptions = await _billingClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);

        return subscriptions
            .Where(s => LiveSubscriptionStates.Values.Contains(s.State, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Guarantees a Maxio customer exists for the eShopOnWeb user. The Maxio
    /// customer reference is the eShopOnWeb user id, which makes the lookup and
    /// creation idempotent: a concurrent or repeated call can never create a
    /// second Maxio customer for the same user.
    /// </summary>
    private async Task<MaxioCustomer> EnsureMaxioCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var reference = user.Id;

        var existing = await _billingClient.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitName(user.UserName ?? user.Email ?? reference);
        var input = new MaxioCustomerInput
        {
            Reference = reference,
            FirstName = firstName,
            LastName = lastName,
            Email = user.Email ?? string.Empty
        };

        try
        {
            return await _billingClient.CreateCustomerAsync(input, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            // Lost a create race (reference values are unique in Maxio): the
            // customer must now exist - re-read it.
            var raced = await _billingClient.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(int maxioCustomerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _billingClient.ListCustomerSubscriptionsAsync(maxioCustomerId, cancellationToken);

        return subscriptions.FirstOrDefault(s =>
            LiveSubscriptionStates.Values.Contains(s.State, StringComparer.OrdinalIgnoreCase)
            && string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<ApplicationUser> ResolveUserAsync(ClaimsPrincipal principal)
    {
        var username = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new UnauthorizedAccessException("No authenticated user identity was found on the request.");
        }

        var user = await _userManager.FindByNameAsync(username);
        if (user is null)
        {
            throw new UnauthorizedAccessException($"User '{username}' no longer exists.");
        }

        return user;
    }

    private static (string FirstName, string LastName) SplitName(string name)
    {
        var trimmed = name.Trim();
        var separatorIndex = trimmed.IndexOfAny(new[] { ' ', '@', '_' });
        if (separatorIndex < 0)
        {
            return (trimmed, "Subscriber");
        }

        return (trimmed[..separatorIndex], trimmed[(separatorIndex + 1)..]);
    }
}

/// <summary>
/// Thrown when a subscribe request references a plan handle that is not part of
/// the configured product family.
/// </summary>
public class UnknownSubscriptionPlanException : Exception
{
    public UnknownSubscriptionPlanException(string productHandle)
        : base($"Unknown subscription plan '{productHandle}'.")
    {
        ProductHandle = productHandle;
    }

    public string ProductHandle { get; }
}
