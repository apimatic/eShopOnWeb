using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscription;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Maxio Advanced Billing implementation of ISubscriptionService. Maxio is the billing
/// system of record: plans are Maxio products in the configured product family, the
/// eShopOnWeb user id is the Maxio customer reference, and subscriptions live in Maxio.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    private const string FamilyProductsCacheKeyPrefix = "maxio-family-products:";
    private static readonly TimeSpan FamilyProductsCacheTtl = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maxio subscription states (per the spec's Subscription-State enum) in which the
    /// customer still holds the plan. Subscribing to a plan already held in one of these
    /// states is a no-op that returns the existing subscription instead of creating a dupe.
    /// </summary>
    private static readonly HashSet<string> LiveSubscriptionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending", "assessing", "trialing", "active", "soft_failure", "past_due",
        "suspended", "unpaid", "trial_ended", "on_hold", "awaiting_signup"
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new();

    private readonly IMaxioClient _maxioClient;
    private readonly IOptions<MaxioOptions> _options;
    private readonly IMemoryCache _cache;

    public MaxioSubscriptionService(IMaxioClient maxioClient, IOptions<MaxioOptions> options, IMemoryCache cache)
    {
        _maxioClient = maxioClient;
        _options = options;
        _cache = cache;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await GetFamilyProductsAsync(cancellationToken);
        return products
            .Where(p => p.ArchivedAt is null)
            .OrderBy(p => p.PriceInCents)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(MapPlan)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(command.UserId, nameof(command.UserId));
        Guard.Against.NullOrEmpty(command.ProductHandle, nameof(command.ProductHandle));

        // Serialize subscribes per user so a double-click can't race past the
        // already-subscribed check and create two subscriptions.
        var userLock = UserLocks.GetOrAdd(command.UserId, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(command, cancellationToken);

            var existing = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var current = existing.FirstOrDefault(s =>
                string.Equals(s.Product?.Handle, command.ProductHandle, StringComparison.OrdinalIgnoreCase) &&
                LiveSubscriptionStates.Contains(s.State));
            if (current is not null)
            {
                return new SubscribeResult(MapSubscription(current), alreadySubscribed: true, customer.Id);
            }

            // The plan must exist (and be live) in the configured product family.
            var products = await GetFamilyProductsAsync(cancellationToken);
            var plan = products.FirstOrDefault(p =>
                string.Equals(p.Handle, command.ProductHandle, StringComparison.OrdinalIgnoreCase));
            if (plan is null || plan.ArchivedAt is not null)
            {
                throw new SubscriptionPlanNotFoundException(command.ProductHandle);
            }

            var subscription = await _maxioClient.CreateSubscriptionAsync(new MaxioSubscriptionCreate
            {
                ProductHandle = plan.Handle,
                CustomerId = customer.Id,
                Reference = BuildSubscriptionReference(command.UserId, plan.Handle!),
                // Sign up without a stored payment method (plans are configured with no card
                // requirement); Maxio remains the billing system of record.
                PaymentCollectionMethod = "remittance"
            }, cancellationToken);

            return new SubscribeResult(MapSubscription(subscription), alreadySubscribed: false, customer.Id);
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));

        // Read-only: do not create a Maxio customer just because the user looked.
        var customer = await _maxioClient.FindCustomerByReferenceAsync(userId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .OrderByDescending(s => s.CreatedAt ?? DateTime.MinValue)
            .Select(MapSubscription)
            .ToList();
    }

    /// <summary>
    /// Idempotently resolves the Maxio customer backing an eShopOnWeb user. The user id is
    /// used as the unique Maxio customer reference, so a double-click (or any repeat call)
    /// never creates a second customer.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscribeCommand command, CancellationToken cancellationToken)
    {
        var customer = await _maxioClient.FindCustomerByReferenceAsync(command.UserId, cancellationToken);
        if (customer is not null)
        {
            return customer;
        }

        try
        {
            return await _maxioClient.CreateCustomerAsync(new MaxioCustomerCreate
            {
                FirstName = string.IsNullOrWhiteSpace(command.FirstName) ? "eShopOnWeb" : command.FirstName,
                LastName = string.IsNullOrWhiteSpace(command.LastName) ? "Subscriber" : command.LastName,
                Email = command.Email,
                Organization = command.UserId,
                Reference = command.UserId
            }, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            // A concurrent call may have created the customer between our lookup and
            // our create; Maxio enforces one customer per reference value, so fall back
            // to reading the winner.
            var winner = await _maxioClient.FindCustomerByReferenceAsync(command.UserId, cancellationToken);
            if (winner is not null)
            {
                return winner;
            }

            throw;
        }
    }

    private async Task<IReadOnlyList<MaxioProduct>> GetFamilyProductsAsync(CancellationToken cancellationToken)
    {
        var options = _options.Value;
        Guard.Against.NullOrEmpty(options.ProductFamilyHandle, nameof(options.ProductFamilyHandle));

        var cacheKey = FamilyProductsCacheKeyPrefix + options.ProductFamilyHandle;
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<MaxioProduct>? cached) && cached is not null)
        {
            return cached;
        }

        // The spec's product_family_id path parameter accepts "handle:{handle}", which
        // matters here because numeric ids are reassigned when the sandbox is re-seeded.
        var products = await _maxioClient.ListProductsForProductFamilyAsync($"handle:{options.ProductFamilyHandle}", cancellationToken);
        _cache.Set(cacheKey, products, FamilyProductsCacheTtl);
        return products;
    }

    private static string BuildSubscriptionReference(string userId, string productHandle) =>
        $"{userId}:{productHandle}";

    private static SubscriptionPlanDto MapPlan(MaxioProduct product) => new()
    {
        ProductId = product.Id,
        Handle = product.Handle ?? string.Empty,
        Name = product.Name,
        Description = product.Description,
        Price = ToPrice(product.PriceInCents),
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? "month",
        ProductFamilyHandle = product.ProductFamily?.Handle ?? string.Empty,
        Archived = product.ArchivedAt is not null
    };

    private static SubscriptionDto MapSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        ProductId = subscription.Product?.Id ?? 0,
        ProductHandle = subscription.Product?.Handle ?? string.Empty,
        ProductName = subscription.Product?.Name ?? string.Empty,
        Price = ToPrice(subscription.ProductPriceInCents > 0
            ? subscription.ProductPriceInCents
            : subscription.Product?.PriceInCents ?? 0),
        CustomerId = subscription.Customer?.Id ?? 0,
        // current_period_ends_at is, per the spec, when the next regularly scheduled
        // charge will occur — the user-facing "next billing date".
        NextBillingAt = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
        ActivatedAt = subscription.ActivatedAt,
        CreatedAt = subscription.CreatedAt,
        CanceledAt = subscription.CanceledAt,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod ?? false
    };

    private static decimal ToPrice(long cents) => cents / 100m;
}