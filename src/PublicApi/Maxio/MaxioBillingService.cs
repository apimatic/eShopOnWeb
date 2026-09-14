using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;
public class MaxioBillingService : IMaxioBillingService
{
    private static readonly TimeSpan CatalogCacheDuration = TimeSpan.FromMinutes(5);

    /// <summary>Subscription states that mean the customer is no longer subscribed to that plan.</summary>
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create",
    };

    private readonly IMaxioApiClient _maxio;
    private readonly MaxioOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _customerGates = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _catalogGate = new(1, 1);

    public MaxioBillingService(IMaxioApiClient maxio, IOptions<MaxioOptions> options, IMemoryCache cache)
    {
        _maxio = maxio;
        _options = options.Value;
        _cache = cache;
    }

    public async Task<IReadOnlyList<MaxioProduct>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        var catalog = await GetCatalogAsync(cancellationToken);
        return catalog.Products;
    }

    public async Task<MaxioSubscription> SubscribeAsync(
        string customerReference,
        MaxioCustomerProfile profile,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new SubscriptionPlanNotFoundException("A plan handle must be provided.");
        }

        EnsureProductFamilyConfigured();

        var gate = _customerGates.GetOrAdd(customerReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await SubscribeCoreAsync(customerReference, profile, planHandle, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsForCustomerAsync(
        string customerReference,
        CancellationToken cancellationToken = default)
    {
        EnsureProductFamilyConfigured();

        if (string.IsNullOrWhiteSpace(customerReference))
        {
            return Array.Empty<MaxioSubscription>();
        }

        var customer = await _maxio.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer?.Id == null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id.Value, cancellationToken);
        if (subscriptions.Count == 0)
        {
            return subscriptions;
        }

        var catalog = await GetCatalogAsync(cancellationToken);
        return subscriptions
            .Where(subscription => SameFamily(subscription.Product?.ProductFamily, catalog.Family))
            .ToList();
    }

    private async Task<MaxioSubscription> SubscribeCoreAsync(
        string customerReference,
        MaxioCustomerProfile profile,
        string planHandle,
        CancellationToken cancellationToken)
    {
        var catalog = await GetCatalogAsync(cancellationToken);
        var plan = catalog.Products.FirstOrDefault(p =>
            string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan == null)
        {
            throw new SubscriptionPlanNotFoundException(
                $"The plan '{planHandle}' is not available in the '{_options.ProductFamilyHandle}' product family.");
        }

        profile.Reference = customerReference;

        var customer = await EnsureCustomerAsync(customerReference, profile, cancellationToken);

        var existing = await _maxio.ListCustomerSubscriptionsAsync(customer.Id!.Value, cancellationToken);

        var activeSamePlan = existing.FirstOrDefault(sub =>
            !IsTerminal(sub.State) &&
            string.Equals(sub.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (activeSamePlan != null)
        {
            return activeSamePlan;
        }

        var activeOtherPlan = existing.FirstOrDefault(sub =>
            !IsTerminal(sub.State) &&
            !string.Equals(sub.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            SameFamily(sub.Product?.ProductFamily, catalog.Family));
        if (activeOtherPlan != null)
        {
            throw new AlreadySubscribedException(
                $"You are already subscribed to '{activeOtherPlan.Product?.Name ?? activeOtherPlan.Product?.Handle}' in the '{_options.ProductFamilyHandle}' product family.");
        }

        var created = await _maxio.CreateSubscriptionAsync(new MaxioCreateSubscriptionData
        {
            ProductHandle = planHandle,
            CustomerId = customer.Id,
            PaymentCollectionMethod = "remittance",
        }, cancellationToken);

        return created;
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(
        string customerReference,
        MaxioCustomerProfile profile,
        CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        try
        {
            return await _maxio.CreateCustomerAsync(profile, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422 && LooksLikeDuplicateReference(ex.Message))
        {
            // Lost a creation race against another process/instance: Maxio enforced
            // its unique-reference constraint. Fall back to reading the winner.
            var winner = await _maxio.FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (winner == null)
            {
                throw;
            }

            return winner;
        }
    }

    private async Task<CatalogSnapshot> GetCatalogAsync(CancellationToken cancellationToken)
    {
        var familyHandle = _options.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(familyHandle))
        {
            throw new MaxioConfigurationException(
                $"Maxio is not configured: '{MaxioOptions.SectionName}:ProductFamilyHandle' (env MAXIO_DEFAULT_PRODUCT_FAMILY) is required.");
        }

        var cacheKey = $"maxio:catalog:{familyHandle}";
        if (_cache.TryGetValue(cacheKey, out CatalogSnapshot? cached) && cached != null)
        {
            return cached;
        }

        await _catalogGate.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue(cacheKey, out cached) && cached != null)
            {
                return cached;
            }

            var family = await _maxio.GetProductFamilyByHandleAsync(familyHandle, cancellationToken);
            var products = family.Id == null
                ? Array.Empty<MaxioProduct>()
                : await _maxio.ListProductsForFamilyAsync(family.Id.Value, cancellationToken);

            var snapshot = new CatalogSnapshot(family, products);
            _cache.Set(cacheKey, snapshot, CatalogCacheDuration);
            return snapshot;
        }
        finally
        {
            _catalogGate.Release();
        }
    }

    private static bool SameFamily(MaxioProductFamily? left, MaxioProductFamily? right)
    {
        if (left == null || right == null)
        {
            return false;
        }

        if (left.Id.HasValue && right.Id.HasValue)
        {
            return left.Id.Value == right.Id.Value;
        }

        var leftHandle = NormalizeHandle(left.Handle);
        var rightHandle = NormalizeHandle(right.Handle);
        return leftHandle != null
            && rightHandle != null
            && string.Equals(leftHandle, rightHandle, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Advanced Billing stores handles case-sensitively and may include separators
    /// (e.g. "eShopSubscribe" vs the configured "eshop-subscribe"). Compare on the
    /// family id when available and fall back to a separator-insensitive handle match.
    /// </summary>
    private static string? NormalizeHandle(string? handle)
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            return null;
        }

        return new string(handle.Where(char.IsLetterOrDigit).ToArray());
    }

    private void EnsureProductFamilyConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new MaxioConfigurationException(
                $"Maxio is not configured: '{MaxioOptions.SectionName}:ProductFamilyHandle' (env MAXIO_DEFAULT_PRODUCT_FAMILY) is required.");
        }
    }

    private static bool IsTerminal(string? state)
    {
        return state != null && TerminalStates.Contains(state);
    }

    private static bool LooksLikeDuplicateReference(string message)
    {
        return message.Contains("reference", StringComparison.OrdinalIgnoreCase)
            && (message.Contains("taken", StringComparison.OrdinalIgnoreCase)
                || message.Contains("unique", StringComparison.OrdinalIgnoreCase)
                || message.Contains("already", StringComparison.OrdinalIgnoreCase)
                || message.Contains("in use", StringComparison.OrdinalIgnoreCase));
    }

    private sealed record CatalogSnapshot(MaxioProductFamily Family, IReadOnlyList<MaxioProduct> Products);
}
