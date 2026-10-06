using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscription;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using UserNotFoundException = Microsoft.eShopWeb.Infrastructure.Identity.UserNotFoundException;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Subscription;

/// <summary>
/// Subscription billing orchestration on top of Maxio Advanced Billing, which is the billing
/// system of record. All identity mapping is stored in Maxio itself (via stable reference
/// values), so the mapping survives restarts even when the app runs on an in-memory database:
/// <list type="bullet">
///   <item>eShopOnWeb user id → Maxio customer reference (unique per Maxio site).</item>
///   <item>user id + plan handle → subscription reference.</item>
/// </list>
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private const string SubscriptionReferencePrefix = "eshop-web";
    private const int MaxReferenceSuffixProbes = 10;

    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending", "failed_to_create", "trialing", "assessing", "active", "soft_failure",
        "past_due", "suspended", "unpaid", "on_hold", "awaiting_signup"
    };

    private static readonly HashSet<string> EndOfLifeStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "trial_ended"
    };

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly MaxioApiClient _maxio;
    private readonly MaxioOptions _options;
    private readonly ILogger<SubscriptionService> _logger;

    /// <summary>
    /// Process-local mutual exclusion per (user, plan) so concurrent double-clicks cannot both
    /// perform lookup-then-create and produce duplicate resources.
    /// </summary>
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _subscribeLocks = new();

    private readonly SemaphoreSlim _siteProbeLock = new(1, 1);
    private string? _cachedPaymentCollectionMethod;

    public SubscriptionService(
        UserManager<ApplicationUser> userManager,
        MaxioApiClient maxio,
        Microsoft.Extensions.Options.IOptions<MaxioOptions> options,
        ILogger<SubscriptionService> logger)
    {
        _userManager = userManager;
        _maxio = maxio;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var products = await _maxio.ListProductsInFamilyAsync(_options.ProductFamilyHandle);

        return products
            .Where(p => string.IsNullOrWhiteSpace(p.ArchivedAt))
            .Select(p => new SubscriptionPlan
            {
                Id = p.Id,
                Handle = p.Handle!,
                Name = p.Name,
                Description = p.Description ?? string.Empty,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit ?? string.Empty,
                ProductFamilyHandle = p.ProductFamily?.Handle ?? _options.ProductFamilyHandle
            })
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<SubscriptionDetails> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("A username is required.", nameof(userName));
        }
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A plan handle is required.", nameof(planHandle));
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user == null)
        {
            throw new UserNotFoundException(userName);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var plans = await GetAvailablePlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan == null)
        {
            throw new PlanNotFoundException(planHandle);
        }

        var lockKey = $"{user.Id}|{plan.Handle}";
        var gate = _subscribeLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await SubscribeLockedAsync(user, plan);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> GetMySubscriptionsAsync(string userName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("A username is required.", nameof(userName));
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user == null)
        {
            throw new UserNotFoundException(userName);
        }

        var customer = await _maxio.LookupCustomerByReferenceAsync(CustomerReferenceFor(user.Id));
        if (customer == null)
        {
            return new List<SubscriptionDetails>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id);
        return subscriptions
            .Select(s => MapSubscription(s))
            .OrderByDescending(s => s.SubscriptionId)
            .ToList();
    }

    private async Task<SubscriptionDetails> SubscribeLockedAsync(ApplicationUser user, SubscriptionPlan plan)
    {
        var customer = await EnsureCustomerAsync(user);

        var subscriptionReference = SubscriptionReferenceFor(user.Id, plan.Handle);
        var existing = await _maxio.LookupSubscriptionByReferenceAsync(subscriptionReference);

        if (existing != null && !EndOfLifeStates.Contains(existing.State))
        {
            // Already enrolled (or enrollment in flight) — this call is a no-op, which makes
            // double-clicks and retries safe.
            _logger.LogInformation(
                "User {UserId} already has subscription {SubscriptionId} for plan {PlanHandle} (state {State}).",
                user.Id, existing.Id, plan.Handle, existing.State);
            return MapSubscription(existing, reference: subscriptionReference);
        }

        if (existing != null)
        {
            // The reference is reserved by an end-of-life subscription, so pick the next free suffix.
            subscriptionReference = await FindFreeSubscriptionReferenceAsync(user.Id, plan.Handle, subscriptionReference);
        }

        _logger.LogInformation(
            "Creating Maxio subscription for user {UserId} on plan {PlanHandle} (customer {CustomerId}, reference {Reference}).",
            user.Id, plan.Handle, customer.Id, subscriptionReference);

        var created = await _maxio.CreateSubscriptionAsync(new MaxioCreateSubscription
        {
            ProductHandle = plan.Handle,
            CustomerId = customer.Id,
            Reference = subscriptionReference,
            PaymentCollectionMethod = await GetPaymentCollectionMethodAsync()
        });

        return MapSubscription(created, reference: subscriptionReference);
    }

    /// <summary>
    /// Idempotently resolves the Maxio customer for an eShopOnWeb user. The reference value is
    /// the stable application user id, and Maxio enforces one customer per reference value, so
    /// the lookup-then-create sequence can never yield two customers.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user)
    {
        var reference = CustomerReferenceFor(user.Id);
        var existing = await _maxio.LookupCustomerByReferenceAsync(reference);
        if (existing != null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitDisplayName(user);
        try
        {
            return await _maxio.CreateCustomerAsync(new MaxioCreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = user.Email ?? user.UserName ?? string.Empty,
                Reference = reference
            });
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            // Lost a race against another instance creating the same reference customer.
            var raced = await _maxio.LookupCustomerByReferenceAsync(reference);
            if (raced != null)
            {
                return raced;
            }
            throw;
        }
    }

    /// <summary>
    /// Subscriptions are created without a payment method (the plans do not require one), which
    /// is only possible with document-based collection: "remittance" on Relationship Invoicing
    /// sites or "invoice" on statement-based sites. The site architecture is read once and cached.
    /// </summary>
    private async Task<string> GetPaymentCollectionMethodAsync()
    {
        if (_cachedPaymentCollectionMethod != null)
        {
            return _cachedPaymentCollectionMethod;
        }

        await _siteProbeLock.WaitAsync();
        try
        {
            if (_cachedPaymentCollectionMethod == null)
            {
                var site = await _maxio.ReadSiteAsync();
                _cachedPaymentCollectionMethod = site.RelationshipInvoicingEnabled ? "remittance" : "invoice";
            }
        }
        finally
        {
            _siteProbeLock.Release();
        }

        return _cachedPaymentCollectionMethod;
    }

    private async Task<string> FindFreeSubscriptionReferenceAsync(string userId, string planHandle, string baseReference)
    {
        for (var suffix = 2; suffix <= MaxReferenceSuffixProbes + 1; suffix++)
        {
            var candidate = $"{baseReference}-{suffix}";
            var probe = await _maxio.LookupSubscriptionByReferenceAsync(candidate);
            if (probe == null)
            {
                return candidate;
            }
            if (!EndOfLifeStates.Contains(probe.State))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"Unable to find a free subscription reference for user '{userId}' on plan '{planHandle}'.");
    }

    private static string CustomerReferenceFor(string userId) => $"{SubscriptionReferencePrefix}-user-{userId}";

    private static string SubscriptionReferenceFor(string userId, string planHandle) =>
        $"{SubscriptionReferencePrefix}-sub-{userId}-{planHandle}";

    private static (string FirstName, string LastName) SplitDisplayName(ApplicationUser user)
    {
        var email = user.Email ?? user.UserName ?? "subscriber";
        var localPart = email.Contains('@') ? email.Substring(0, email.IndexOf('@')) : email;
        var namePart = System.Text.RegularExpressions.Regex.Split(localPart, "[._-]+")
            .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)) ?? "eShop";
        return (char.ToUpperInvariant(namePart[0]) + namePart[1..], "Customer");
    }

    private SubscriptionDetails MapSubscription(MaxioSubscription subscription, string? reference = null)
    {
        return new SubscriptionDetails
        {
            SubscriptionId = subscription.Id,
            State = subscription.State,
            PlanName = subscription.Product?.Name ?? string.Empty,
            PlanHandle = subscription.Product?.Handle ?? string.Empty,
            PriceInCents = subscription.Product?.PriceInCents ?? subscription.ProductPriceInCents ?? 0,
            Interval = subscription.Product?.Interval ?? 1,
            IntervalUnit = subscription.Product?.IntervalUnit ?? string.Empty,
            ActivatedAt = ParseDate(subscription.ActivatedAt),
            NextBillingDate = ParseDate(subscription.CurrentPeriodEndsAt) ?? ParseDate(subscription.NextAssessmentAt),
            CanceledAt = ParseDate(subscription.CanceledAt),
            CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod ?? false,
            CustomerId = subscription.Customer?.Id ?? 0,
            CustomerReference = subscription.Customer?.Reference ?? string.Empty,
            Reference = subscription.Reference ?? reference ?? string.Empty
        };
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
}