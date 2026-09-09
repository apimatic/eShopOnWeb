using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscription;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Orchestrates subscription billing against Maxio Advanced Billing, which is the billing system
/// of record: no subscription state is persisted locally, so the storefront merely mirrors what
/// Maxio holds.
///
/// Idempotency (double-click safety):
///  - The Maxio customer is keyed by the eShopOnWeb username via the spec's customer "reference"
///    field. Enrolling resolves the customer by reference first and only creates one when absent,
///    guarded by a per-user lock so concurrent requests cannot create duplicates.
///  - Subscribing to a plan the user already holds (any live state) returns the existing
///    subscription instead of creating a second one.
/// </summary>
internal class SubscriptionBillingService : ISubscriptionBillingService
{
    /// <summary>
    /// Subscription states from the spec (Subscription-State.yaml) under which the user is
    /// considered to hold the plan. End-of-life states (canceled, expired, failed_to_create)
    /// free the user up to subscribe again.
    /// </summary>
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending", "trialing", "assessing", "active", "awaiting_signup",
        "soft_failure", "past_due", "suspended", "unpaid", "on_hold"
    };

    private readonly MaxioClient _maxio;
    private readonly MaxioOptions _options;
    private readonly ILogger<SubscriptionBillingService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _userLocks = new(StringComparer.Ordinal);

    public SubscriptionBillingService(MaxioClient maxio, IOptions<MaxioOptions> options, ILogger<SubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _maxio.ListAllProductsAsync(perPage: 100, maxPages: 10, cancellationToken);

        return products
            .Where(p => !string.IsNullOrEmpty(p.Handle))
            .Where(p => string.Equals(p.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
            .Where(p => string.IsNullOrEmpty(p.ArchivedAt))
            .Select(p => new SubscriptionPlan
            {
                Handle = p.Handle!,
                Name = p.Name,
                Description = p.Description,
                PriceInCents = p.PriceInCents ?? 0,
                Interval = p.Interval ?? 1,
                IntervalUnit = p.IntervalUnit ?? "month",
                RequiresPaymentMethod = p.RequireCreditCard == true,
                ProductFamilyHandle = p.ProductFamily?.Handle ?? string.Empty
            })
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<SubscriptionSummary> SubscribeAsync(string userId, string email, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("A user id is required.", nameof(userId));
        }
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A plan handle is required.", nameof(planHandle));
        }

        var plans = await ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            ?? throw new MaxioPlanNotFoundException(planHandle);

        var customer = await EnsureCustomerAsync(userId, email, cancellationToken);

        var existing = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("User {UserId} already holds subscription {SubscriptionId} for plan {PlanHandle}; returning it as-is.",
                userId, existing.Id, plan.Handle);
            var replayed = MapSubscription(existing);
            replayed.ExistingReturned = true;
            return replayed;
        }

        _logger.LogInformation("Enrolling user {UserId} (customer {CustomerId}) in plan {PlanHandle}.",
            userId, customer.Id, plan.Handle);
        var created = await _maxio.CreateSubscriptionAsync(
            customer.Id,
            plan.Handle,
            reference: $"{userId}:{plan.Handle}",
            requiresPaymentMethod: plan.RequiresPaymentMethod,
            cancellationToken);

        return MapSubscription(created);
    }

    public async Task<IReadOnlyList<SubscriptionSummary>> ListUserSubscriptionsAsync(string userId, string email, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Array.Empty<SubscriptionSummary>();
        }

        // Read path: never create a billing customer as a side effect of listing.
        var customer = await _maxio.GetCustomerByReferenceAsync(userId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionSummary>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .Where(s => s.State is not null && LiveStates.Contains(s.State))
            .Select(MapSubscription)
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string userId, string email, CancellationToken cancellationToken)
    {
        var userLock = _userLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken);
        try
        {
            var customer = await _maxio.GetCustomerByReferenceAsync(userId, cancellationToken);
            if (customer is not null)
            {
                return customer;
            }

            var (firstName, lastName) = DeriveCustomerName(email);
            _logger.LogInformation("Creating Maxio customer for user {UserId} (reference '{Reference}').", userId, userId);
            return await _maxio.CreateCustomerAsync(firstName, lastName, email, reference: userId, cancellationToken);
        }
        finally
        {
            userLock.Release();
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            s.State is not null && LiveStates.Contains(s.State));
    }

    private SubscriptionSummary MapSubscription(MaxioSubscription subscription)
    {
        return new SubscriptionSummary
        {
            Id = subscription.Id,
            State = subscription.State ?? string.Empty,
            PlanHandle = subscription.Product?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? string.Empty,
            PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0,
            NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
            ActivatedAt = subscription.ActivatedAt,
            CanceledAt = subscription.CanceledAt,
            CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod == true,
            BillingCustomerId = subscription.Customer?.Id ?? 0
        };
    }

    /// <summary>
    /// Maxio requires first/last name on customer creation; eShopOnWeb identity only carries an
    /// email/username, so derive a sensible human-readable name from the local part.
    /// </summary>
    private static (string FirstName, string LastName) DeriveCustomerName(string email)
    {
        var localPart = email.Split('@')[0];
        var pieces = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);

        var firstName = Capitalize(pieces.FirstOrDefault() ?? "eShop");
        var lastName = Capitalize(pieces.Skip(1).FirstOrDefault() ?? "User");
        return (firstName, lastName);
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "User";
        }
        return char.ToUpperInvariant(value[0]) + value[1..];
    }
}
