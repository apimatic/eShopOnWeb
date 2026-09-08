using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class MaxioSubscriptionService : ISubscriptionService
{
    // Maxio subscription states that mean the subscription is still in force.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired"
    };

    private readonly IMaxioApiClient _client;
    private readonly IMemoryCache _cache;
    private readonly IOptions<MaxioOptions> _options;
    private readonly object _plansCacheLock = new();

    public MaxioSubscriptionService(IMaxioApiClient client, IMemoryCache cache, IOptions<MaxioOptions> options)
    {
        _client = client;
        _cache = cache;
        _options = options;
    }

    public async Task<IReadOnlyList<MaxioProduct>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var family = await ResolveProductFamilyAsync(cancellationToken);

        var cacheKey = $"Maxio:Products:{family.Id}";
        if (_cache.TryGetValue(cacheKey, out List<MaxioProduct>? cached) && cached != null)
        {
            return cached;
        }

        var plans = await _client.ListProductsForFamilyAsync(family.Id, cancellationToken);
        var nonArchived = plans
            .Where(p => p.ArchivedAt == null)
            .OrderBy(p => p.PriceInCents)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        lock (_plansCacheLock)
        {
            _cache.Set(cacheKey, nonArchived, TimeSpan.FromMinutes(5));
        }

        return nonArchived;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var customer = await _client.FindCustomerByReferenceAsync(user.Id, cancellationToken);
        if (customer == null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        var subscriptions = await _client.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
        return subscriptions
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
    }

    public async Task<MaxioSubscription> SubscribeAsync(ApplicationUser user, string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A subscription plan handle is required.", nameof(planHandle));
        }

        var plan = await FindPlanByHandleAsync(planHandle, cancellationToken)
                   ?? throw new SubscriptionPlanNotFoundException(planHandle);

        var customer = await EnsureCustomerAsync(user, cancellationToken);
        var reference = BuildSubscriptionReference(user, planHandle);

        // Serialize subscribe attempts per eShop user within this process so a doubled
        // click on the client cannot slip two creates past the check-then-act below.
        var gate = KeyedGates.GetOrAdd(customer.Reference ?? user.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = await FindOpenSubscriptionAsync(customer.Id, plan, cancellationToken);
            if (existing != null)
            {
                return existing;
            }

            try
            {
                return await _client.CreateSubscriptionAsync(new MaxioCreateSubscriptionRequest
                {
                    Subscription = new MaxioCreateSubscriptionRequest.SubscriptionDraft
                    {
                        ProductHandle = plan.Handle,
                        CustomerId = customer.Id,
                        PaymentCollectionMethod = "remittance",
                        Reference = reference
                    }
                }, cancellationToken);
            }
            catch (MaxioApiException ex) when (ex.IsReferenceConflict)
            {
                // A concurrent identical request (another instance, or the gate was not
                // yet involved) already created the subscription - return theirs.
                var winner = await FindOpenSubscriptionAsync(customer.Id, plan, cancellationToken);
                if (winner != null)
                {
                    return winner;
                }

                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<MaxioSubscription?> FindOpenSubscriptionAsync(long customerId, MaxioProduct plan, CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListSubscriptionsForCustomerAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            !TerminalStates.Contains(s.State ?? string.Empty) &&
            string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<MaxioProduct?> FindPlanByHandleAsync(string planHandle, CancellationToken cancellationToken)
    {
        var plans = await GetPlansAsync(cancellationToken);
        return plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var customer = await _client.FindCustomerByReferenceAsync(user.Id, cancellationToken);
        if (customer != null)
        {
            return customer;
        }

        try
        {
            return await _client.CreateCustomerAsync(new MaxioCreateCustomerRequest
            {
                Customer = new MaxioCreateCustomerRequest.CustomerDraft
                {
                    FirstName = NameParts.First(user),
                    LastName = NameParts.Last(user),
                    Organization = "eShopOnWeb",
                    Email = user.Email ?? user.UserName,
                    Reference = user.Id
                }
            }, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.IsReferenceConflict)
        {
            // A concurrent identical request created the customer first.
            return await _client.FindCustomerByReferenceAsync(user.Id, cancellationToken)
                   ?? throw new MaxioApiException(System.Net.HttpStatusCode.Conflict,
                       new[] { "A customer was created concurrently but could not be retrieved." }, null);
        }
    }

    private async Task<ProductFamilyDto> ResolveProductFamilyAsync(CancellationToken cancellationToken)
    {
        var familyHandle = _options.Value.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(familyHandle))
        {
            throw new MaxioConfigurationException(
                "Maxio is not configured: 'Maxio:ProductFamilyHandle' is missing. Set it from the " +
                "MAXIO_DEFAULT_PRODUCT_FAMILY environment variable (user-secrets).");
        }

        var families = await _client.ListProductFamiliesAsync(cancellationToken);
        return families.FirstOrDefault(f => string.Equals(f.Handle, familyHandle, StringComparison.OrdinalIgnoreCase))
               ?? throw new MaxioProductFamilyNotFoundException(familyHandle);
    }

    private static string BuildSubscriptionReference(ApplicationUser user, string planHandle) =>
        $"eshop:{user.Id}:{planHandle}";
}

internal static class NameParts
{
    public static string First(ApplicationUser user)
    {
        var local = Split(user).local;
        return string.IsNullOrWhiteSpace(local) ? (user.UserName ?? "eShop") : local;
    }

    public static string Last(ApplicationUser user)
    {
        var domain = Split(user).domain;
        return string.IsNullOrWhiteSpace(domain) ? "User" : domain;
    }

    private static (string? local, string? domain) Split(ApplicationUser user)
    {
        var address = user.Email ?? user.UserName;
        if (string.IsNullOrWhiteSpace(address))
        {
            return (null, null);
        }

        var at = address.IndexOf('@');
        return at < 0
            ? (address, null)
            : (address[..at], address[(at + 1)..]);
    }
}
