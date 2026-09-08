using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <inheritdoc />
public class SubscriptionManager : ISubscriptionManager
{
    /// <summary>
    /// Payment collection method used when enrolling without a stored payment method. The
    /// "payment method not required" plans in the demo catalog subscribe in "remittance" mode
    /// (invoice-style) so no card capture / 3-DS is involved. Value comes from the spec's
    /// Collection-Method schema.
    /// </summary>
    private const string RemittanceCollectionMethod = "remittance";

    private static readonly TimeSpan PlanCatalogCacheDuration = TimeSpan.FromSeconds(60);

    /// <summary>Subscriptions in any state other than these end-of-life states are "live".</summary>
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create",
        "trial_ended"
    };

    // Per-key in-process locks so a double-click can never enroll twice concurrently.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EnrollmentGates = new();

    private readonly IMaxioApiClient _client;
    private readonly IMemoryCache _cache;
    private readonly IOptions<MaxioOptions> _options;

    public SubscriptionManager(IMaxioApiClient client, IMemoryCache cache, IOptions<MaxioOptions> options)
    {
        _client = client;
        _cache = cache;
        _options = options;
    }

    public async Task<IReadOnlyList<MaxioProduct>> GetSubscriptionPlansAsync(CancellationToken ct = default)
    {
        var familyHandle = ResolveProductFamilyHandle();
        var cacheKey = BuildPlanCacheKey(familyHandle);

        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<MaxioProduct>? cached) && cached != null)
        {
            return cached;
        }

        var plans = await _client.ListProductsForProductFamilyAsync(familyHandle, ct);

        _cache.Set(cacheKey, plans, PlanCatalogCacheDuration);
        return plans;
    }

    public async Task<MaxioCustomer> EnsureCustomerAsync(string userName, CancellationToken ct = default)
    {
        var reference = MaxioCustomerReference.Create(userName);
        var gate = GetGate(EnsureGateKey(reference));
        await gate.WaitAsync(ct);
        try
        {
            var existing = await _client.FindCustomerByReferenceAsync(reference, ct);
            if (existing != null)
            {
                return existing;
            }

            var profile = MaxioCustomerReference.Profile(userName);
            try
            {
                return await _client.CreateCustomerAsync(new MaxioCreateCustomer
                {
                    FirstName = profile.FirstName,
                    LastName = profile.LastName,
                    Email = profile.Email,
                    Organization = "eShopOnWeb",
                    Reference = reference
                }, ct);
            }
            catch (MaxioApiException ex) when (ex.StatusCode == 422 && IsDuplicateReferenceError(ex))
            {
                // Another concurrent enrollment created the customer between our lookup and
                // create. Fetch and return it instead of failing.
                var retried = await _client.FindCustomerByReferenceAsync(reference, ct);
                if (retried != null)
                {
                    return retried;
                }

                throw;
            }
        }
        finally
        {
            ReleaseGate(EnsureGateKey(reference), gate);
        }
    }

    public async Task<MaxioCustomer?> FindCustomerAsync(string userName, CancellationToken ct = default)
    {
        var reference = MaxioCustomerReference.Create(userName);
        return await _client.FindCustomerByReferenceAsync(reference, ct);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(string userName, CancellationToken ct = default)
    {
        var customer = await FindCustomerAsync(userName, ct);
        if (customer == null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        return await _client.ListCustomerSubscriptionsAsync(customer.Id, ct);
    }

    public async Task<SubscriptionEnrollmentResult> SubscribeAsync(string userName, string planHandle, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A plan handle is required.", nameof(planHandle));
        }

        var plan = await FindPlanByHandleAsync(planHandle, ct)
            ?? throw new SubscriptionPlanNotFoundException(planHandle);

        // The customer must exist before the idempotency check below (subscriptions hang off a
        // Maxio customer). EnsureCustomerAsync is itself concurrency-safe.
        await EnsureCustomerAsync(userName, ct);
        var reference = MaxioCustomerReference.Create(userName);

        var gate = GetGate(EnrollGateKey(reference, plan.Handle ?? planHandle));
        await gate.WaitAsync(ct);
        try
        {
            var existing = await FindLiveSubscriptionAsync(reference, plan, ct);
            if (existing != null)
            {
                return new SubscriptionEnrollmentResult(existing, created: false);
            }

            var created = await _client.CreateSubscriptionAsync(new MaxioCreateSubscription
            {
                ProductHandle = plan.Handle ?? planHandle,
                CustomerReference = reference,
                PaymentCollectionMethod = RemittanceCollectionMethod
            }, ct);

            return new SubscriptionEnrollmentResult(created, created: true);
        }
        finally
        {
            ReleaseGate(EnrollGateKey(reference, plan.Handle ?? planHandle), gate);
        }
    }

    private async Task<MaxioProduct?> FindPlanByHandleAsync(string planHandle, CancellationToken ct)
    {
        var plans = await GetSubscriptionPlansAsync(ct);
        return plans.FirstOrDefault(p =>
            string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(string reference, MaxioProduct plan, CancellationToken ct)
    {
        var customer = await _client.FindCustomerByReferenceAsync(reference, ct);
        if (customer == null)
        {
            return null;
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, ct);
        return subscriptions.FirstOrDefault(s =>
            IsLiveState(s.State)
            && string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLiveState(string? state)
    {
        return !string.IsNullOrWhiteSpace(state) && !TerminalStates.Contains(state);
    }

    private string ResolveProductFamilyHandle()
    {
        var handle = _options.Value.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new MaxioConfigurationException(
                "Maxio is not configured: 'Maxio:ProductFamilyHandle' is missing.");
        }

        return handle;
    }

    private static string BuildPlanCacheKey(string familyHandle) => $"maxio:plans:{familyHandle}";

    private static string EnsureGateKey(string reference) => $"ensure:{reference}";

    private static string EnrollGateKey(string reference, string planHandle) => $"enroll:{reference}:{planHandle.ToLowerInvariant()}";

    private static SemaphoreSlim GetGate(string key)
    {
        return EnrollmentGates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
    }

    private static void ReleaseGate(string key, SemaphoreSlim gate)
    {
        gate.Release();

        // Opportunistically drop the gate once it is idle so the dictionary cannot grow without
        // bound over the lifetime of the process.
        if (gate.CurrentCount == 1 && EnrollmentGates.TryGetValue(key, out var current) && ReferenceEquals(current, gate))
        {
            EnrollmentGates.TryRemove(key, out _);
        }
    }

    private static bool IsDuplicateReferenceError(MaxioApiException exception)
    {
        return exception.Errors.Any(e =>
            e.IndexOf("reference", StringComparison.OrdinalIgnoreCase) >= 0
            && (e.IndexOf("taken", StringComparison.OrdinalIgnoreCase) >= 0
                || e.IndexOf("unique", StringComparison.OrdinalIgnoreCase) >= 0));
    }
}
