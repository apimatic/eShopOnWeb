using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Orchestrates the "Subscribe" hero flow against Maxio Advanced Billing as the system of record.
///
/// The Maxio customer for an eShopOnWeb user is keyed by the user's account name (the value of the
/// JWT <c>ClaimTypes.Name</c>) through the customer <c>reference</c> field, which Maxio enforces as
/// unique. Ensuring a customer and subscribing are both idempotent so a double-click cannot create
/// duplicate customers or duplicate subscriptions in a single running instance.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    // Subscription states in which the plan is still being delivered. Everything else
    // (canceled, expired, trial_ended, awaiting_signup, ...) means the plan is not current.
    private static readonly HashSet<string> EndedStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "trial_ended",
        "awaiting_signup"
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> PerPlanLocks = new();

    private static readonly TimeSpan PlansCacheDuration = TimeSpan.FromMinutes(5);

    private readonly IMaxioClient _maxioClient;
    private readonly IMemoryCache _memoryCache;
    private readonly string _cacheKeyPrefix;

    public SubscriptionService(IMaxioClient maxioClient, IMemoryCache memoryCache, IOptions<MaxioSettings> settings)
    {
        _maxioClient = maxioClient;
        _memoryCache = memoryCache;
        _cacheKeyPrefix = BuildCacheKeyPrefix(settings.Value);
    }

    private static string BuildCacheKeyPrefix(MaxioSettings settings)
    {
        try
        {
            return $"maxio:{settings.ResolveBaseUrl()}:{settings.ProductFamilyHandle}";
        }
        catch (MaxioConfigurationException)
        {
            // Settings are validated when the HTTP client is actually used; before that we can
            // still serve from a per-process cache key.
            return "maxio:not-configured";
        }
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var cacheKey = $"{_cacheKeyPrefix}:plans";
        if (_memoryCache.TryGetValue(cacheKey, out IReadOnlyList<MaxioProduct>? cached) && cached is not null)
        {
            return cached;
        }

        var plans = await _maxioClient.ListProductsAsync(cancellationToken);
        var available = plans
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .ToList();

        _memoryCache.Set(cacheKey, (IReadOnlyList<MaxioProduct>)available, PlansCacheDuration);
        return available;
    }

    public async Task<SubscriptionEnrollmentResult> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A plan handle is required.", nameof(planHandle));
        }

        var plan = await FindPlanAsync(planHandle, cancellationToken);

        var customer = await EnsureCustomerAsync(userName, cancellationToken);

        // Serialize the check-then-create inside one running instance so two rapid
        // requests (e.g. a double-click) cannot both observe "no subscription yet".
        var semaphore = PerPlanLocks.GetOrAdd(
            $"{userName}\u0000{planHandle.ToLowerInvariant()}",
            _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(cancellationToken);
        try
        {
            var existing = await FindOngoingSubscriptionAsync(customer.Id, plan.Handle!, cancellationToken);
            if (existing is not null)
            {
                return new SubscriptionEnrollmentResult(existing, wasAlreadySubscribed: true);
            }

            var created = await _maxioClient.CreateSubscriptionAsync(
                new MaxioCreateSubscriptionRequest(plan.Handle!, customer.Reference ?? customer.Email ?? userName),
                cancellationToken);

            return new SubscriptionEnrollmentResult(created, wasAlreadySubscribed: false);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListMySubscriptionsAsync(string userName, CancellationToken cancellationToken)
    {
        var customer = await _maxioClient.FindCustomerByReferenceAsync(userName, cancellationToken);
        if (customer is null)
        {
            return new List<MaxioSubscription>();
        }

        return await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<MaxioProduct> FindPlanAsync(string planHandle, CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        return plan;
    }

    private async Task<MaxioSubscription?> FindOngoingSubscriptionAsync(long customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions
            .Where(s => IsOngoing(s) && string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefault();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string userName, CancellationToken cancellationToken)
    {
        var existing = await _maxioClient.FindCustomerByReferenceAsync(userName, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var customer = CustomerIdentity.CreateCustomer(userName);
        try
        {
            return await _maxioClient.CreateCustomerAsync(customer, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.UpstreamStatusCode == 422)
        {
            // A concurrent request created the same reference between our lookup and create.
            // Maxio enforces reference uniqueness, so re-read instead of retrying the create.
            var createdElsewhere = await _maxioClient.FindCustomerByReferenceAsync(userName, cancellationToken);
            if (createdElsewhere is not null)
            {
                return createdElsewhere;
            }

            throw;
        }
    }

    private static bool IsOngoing(MaxioSubscription subscription) =>
        !string.IsNullOrWhiteSpace(subscription.State) && !EndedStates.Contains(subscription.State);
}
