using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Subscriptions.Maxio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Production-grade orchestration over the Maxio Advanced Billing API.
///
/// Idempotency model (verified against the live Advanced Billing API):
///  - One Maxio customer per eShopOnWeb user, addressed by the deterministic customer
///    reference "eshopweb:{userName}". Looked up via /customers/lookup.json?reference=
///    (HTTP 200 = exists, HTTP 404 = create) so a double-click can never produce two
///    customers; concurrent first calls are serialized by an in-process per-user lock.
///  - One active subscription per (user, plan), addressed by the deterministic
///    subscription reference "eshopweb:{userName}:{planHandle}". Before creating, the
///    customer's subscriptions are checked for a live one with the same reference.
/// </summary>
public sealed class SubscriptionService : ISubscriptionService
{
    /// <summary>Maxio subscription states that represent a subscription still "in force".</summary>
    private static readonly HashSet<string> LiveSubscriptionStates = new(StringComparer.Ordinal)
    {
        "active", "trialing", "assessing", "pending", "past_due", "soft_failure", "unpaid", "on_hold"
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new(StringComparer.Ordinal);

    private readonly MaxioClient _maxioClient;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<SubscriptionService> _logger;
    private readonly MaxioOptions _options;

    public SubscriptionService(
        MaxioClient maxioClient,
        UserManager<ApplicationUser> userManager,
        ILogger<SubscriptionService> logger,
        IOptions<MaxioOptions> options)
    {
        _maxioClient = maxioClient;
        _userManager = userManager;
        _logger = logger;
        _options = options.Value;
    }

    public static string GetCustomerReference(string userName) => $"eshopweb:{userName}";

    public static string GetSubscriptionReference(string userName, string planHandle) =>
        $"eshopweb:{userName}:{planHandle}";

    public async Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _maxioClient.ListProductsAsync(cancellationToken);

        var plans = products
            .Where(p => string.Equals(p.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
            .Select(p => new SubscriptionPlan(
                Handle: p.Handle ?? string.Empty,
                Name: p.Name ?? p.Handle ?? string.Empty,
                Price: CentsToPrice(p.PriceInCents),
                PriceFormatted: FormatPrice(p.PriceInCents),
                Interval: p.Interval,
                IntervalUnit: p.IntervalUnit ?? "month",
                ProductFamilyHandle: p.ProductFamily!.Handle!))
            .OrderBy(p => p.Price)
            .ToList();

        if (plans.Count == 0)
        {
            _logger.LogWarning(
                "No subscription plans found in Maxio product family '{FamilyHandle}'.",
                _options.ProductFamilyHandle);
        }

        return plans;
    }

    public async Task<SubscribeResult> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("A user name is required.", nameof(userName));
        }
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A subscription plan handle is required.", nameof(planHandle));
        }

        // Validate the plan up-front against the configured family so unknown handles
        // fail fast with a 404 instead of a Maxio 422 deep inside the flow.
        var plans = await GetAvailablePlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            ?? throw new SubscriptionPlanNotFoundException(planHandle);

        var subscriptionLock = UserLocks.GetOrAdd(userName, _ => new SemaphoreSlim(1, 1));
        await subscriptionLock.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureMaxioCustomerAsync(userName, cancellationToken);

            // Idempotency pre-check: reuse an existing live subscription with the same
            // deterministic reference instead of creating a duplicate on a double-click.
            var subscriptionReference = GetSubscriptionReference(userName, plan.Handle);
            var existingSubscriptions = await _maxioClient.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
            var existing = existingSubscriptions.FirstOrDefault(s =>
                string.Equals(s.Reference, subscriptionReference, StringComparison.Ordinal)
                && s.State is not null
                && LiveSubscriptionStates.Contains(s.State));

            if (existing is not null)
            {
                _logger.LogInformation(
                    "User {UserName} already has subscription {SubscriptionId} to plan {PlanHandle}; returning it.",
                    userName, existing.Id, plan.Handle);

                return new SubscribeResult(ToDetails(existing), WasAlreadySubscribed: true);
            }

            var created = await _maxioClient.CreateSubscriptionAsync(customer.Id, plan.Handle, subscriptionReference, cancellationToken);

            _logger.LogInformation(
                "User {UserName} subscribed to plan {PlanHandle}: Maxio subscription {SubscriptionId} (state {State}).",
                userName, plan.Handle, created.Id, created.State);

            return new SubscribeResult(ToDetails(created), WasAlreadySubscribed: false);
        }
        finally
        {
            subscriptionLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsForUserAsync(string userName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Array.Empty<SubscriptionDetails>();
        }

        var customer = await _maxioClient.LookupCustomerByReferenceAsync(GetCustomerReference(userName), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDetails>();
        }

        var subscriptions = await _maxioClient.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);

        return subscriptions
            .Select(ToDetails)
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
    }

    /// <summary>
    /// Returns the Maxio customer for the user, creating it on first use. The lookup-by-reference
    /// roundtrip makes customer creation idempotent across requests and restarts.
    /// </summary>
    private async Task<MaxioCustomer> EnsureMaxioCustomerAsync(string userName, CancellationToken cancellationToken)
    {
        var reference = GetCustomerReference(userName);

        var existing = await _maxioClient.LookupCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            throw new InvalidOperationException($"User '{userName}' does not exist.");
        }

        var (firstName, lastName) = SplitName(userName);
        var created = await _maxioClient.CreateCustomerAsync(
            firstName, lastName, user.Email ?? userName, reference, cancellationToken);

        _logger.LogInformation(
            "Created Maxio customer {CustomerId} (reference '{Reference}') for user {UserName}.",
            created.Id, reference, userName);

        return created;
    }

    private static (string FirstName, string LastName) SplitName(string userName)
    {
        // Derive display-friendly names from the eShopOnWeb username (e.g. an email address).
        var localPart = userName.Contains('@', StringComparison.Ordinal)
            ? userName[..userName.IndexOf('@', StringComparison.Ordinal)]
            : userName;
        localPart = localPart.Replace('.', ' ').Replace('_', ' ').Trim();
        var parts = localPart.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var firstName = parts.Length > 0 ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(parts[0]) : "eShop";
        var lastName = parts.Length > 1 ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(parts[^1]) : "Web User";
        return (firstName, lastName);
    }

    private static SubscriptionDetails ToDetails(MaxioSubscription subscription)
    {
        var priceCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0;

        return new SubscriptionDetails(
            MaxioSubscriptionId: subscription.Id,
            State: subscription.State ?? "unknown",
            PlanHandle: subscription.Product?.Handle ?? string.Empty,
            PlanName: subscription.Product?.Name ?? string.Empty,
            Price: CentsToPrice(priceCents),
            PriceFormatted: FormatPrice(priceCents),
            // Advanced Billing surfaces the next renewal on current_period_ends_at
            // (next_billing_at is null for remittance/remittance-style signups).
            NextBillingDate: subscription.CurrentPeriodEndsAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
            CustomerReference: subscription.Customer?.Reference ?? string.Empty,
            PaymentCollectionMethod: subscription.PaymentCollectionMethod ?? string.Empty,
            ActivatedAt: subscription.ActivatedAt,
            CreatedAt: subscription.CreatedAt);
    }

    private static decimal CentsToPrice(int cents) => cents / 100m;

    private static string FormatPrice(int cents) =>
        CentsToPrice(cents).ToString("C", CultureInfo.GetCultureInfo("en-US"));
}
