using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Application service that turns an eShopOnWeb shopper identity into a Maxio relationship
/// (customer + subscription). All operations are idempotent: the Maxio customer is keyed by a
/// unique reference, and subscription creation is guarded both in-process and with Maxio's
/// uniqueness-token support so a double submit never creates two customers/subscriptions.
/// </summary>
public interface IMaxioBillingService
{
    Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken);

    /// <summary>Plans (products) published in the configured product family that can be subscribed to.</summary>
    Task<IReadOnlyList<MaxioProduct>> ListAvailablePlansAsync(CancellationToken cancellationToken);

    /// <summary>Returns the Maxio customer for a shopper reference, or null when they have never subscribed.</summary>
    Task<MaxioCustomer?> FindCustomerAsync(string customerReference, CancellationToken cancellationToken);

    /// <summary>Returns all subscriptions for a shopper reference (empty when they have no Maxio customer yet).</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsAsync(string customerReference, CancellationToken cancellationToken);

    /// <summary>
    /// Ensures a Maxio customer exists for the shopper and subscribes them to the requested plan.
    /// Re-subscribing to the plan they are already on returns the existing subscription (Created=false).
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(string customerReference, string email, string planHandle, CancellationToken cancellationToken);
}

public sealed class SubscribeResult
{
    public SubscribeResult(MaxioSubscription subscription, bool created)
    {
        Subscription = subscription;
        Created = created;
    }

    public MaxioSubscription Subscription { get; }

    /// <summary>True when a new subscription was created; false when an existing matching subscription was returned.</summary>
    public bool Created { get; }
}

public class MaxioBillingService : IMaxioBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscriptionsLocks = new(StringComparer.OrdinalIgnoreCase);

    private const string SiteCacheKey = "maxio:site";
    private static readonly TimeSpan SiteCacheDuration = TimeSpan.FromHours(1);

    private readonly IMaxioBillingClient _client;
    private readonly IOptions<MaxioOptions> _options;
    private readonly IMemoryCache _cache;

    public MaxioBillingService(IMaxioBillingClient client, IOptions<MaxioOptions> options, IMemoryCache cache)
    {
        _client = client;
        _options = options;
        _cache = cache;
    }

    public async Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue<MaxioSite>(SiteCacheKey, out var cached) && cached != null)
        {
            return cached;
        }

        var site = await _client.GetSiteAsync(cancellationToken);
        _cache.Set(SiteCacheKey, site, SiteCacheDuration);
        return site;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListAvailablePlansAsync(CancellationToken cancellationToken)
    {
        var familyHandle = _options.Value.RequireProductFamilyHandle();
        var cacheKey = $"maxio:plans:{familyHandle}";

        if (_cache.TryGetValue<List<MaxioProduct>>(cacheKey, out var cached) && cached != null)
        {
            return cached;
        }

        var products = (await _client.ListProductsByFamilyAsync(familyHandle, cancellationToken))
            .Where(product => product.Handle != null && product.ArchivedAt == null)
            .OrderBy(product => product.PriceInCents)
            .ToList();

        _cache.Set(cacheKey, products, TimeSpan.FromMinutes(1));
        return products;
    }

    public async Task<MaxioCustomer?> FindCustomerAsync(string customerReference, CancellationToken cancellationToken)
    {
        return await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsAsync(string customerReference, CancellationToken cancellationToken)
    {
        var customer = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer == null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        return await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    public async Task<SubscribeResult> SubscribeAsync(string customerReference, string email, string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(customerReference))
        {
            throw new ArgumentException("A customer reference is required.", nameof(customerReference));
        }

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A plan handle is required.", nameof(planHandle));
        }

        // Serialize subscribe attempts per shopper so two rapid clicks on the same button cannot
        // both pass the "no active subscription" check before either creates one.
        var gate = SubscriptionsLocks.GetOrAdd(customerReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var customer = await EnsureCustomerAsync(customerReference, email, cancellationToken);

            var plan = (await ListAvailablePlansAsync(cancellationToken))
                .FirstOrDefault(product =>
                    product.Handle != null &&
                    product.Handle.Equals(planHandle, StringComparison.OrdinalIgnoreCase) &&
                    product.ArchivedAt == null);

            if (plan == null)
            {
                throw new SubscriptionPlanNotFoundException(planHandle);
            }

            var canonicalPlanHandle = plan.Handle!;

            var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var liveSubscriptions = subscriptions
                .Where(subscription => SubscriptionStates.IsLive(subscription.State))
                .ToList();

            var existingForPlan = liveSubscriptions.FirstOrDefault(subscription =>
                subscription.Product?.Handle?.Equals(canonicalPlanHandle, StringComparison.OrdinalIgnoreCase) == true);

            if (existingForPlan != null)
            {
                return new SubscribeResult(existingForPlan, created: false);
            }

            var otherLiveSubscription = liveSubscriptions.FirstOrDefault();
            if (otherLiveSubscription != null)
            {
                throw new AlreadySubscribedException(otherLiveSubscription.Product?.Name ?? otherLiveSubscription.Product?.Handle ?? "another plan");
            }

            var site = await GetSiteAsync(cancellationToken);
            var collectionMethod = site.RelationshipInvoicingEnabled ? "remittance" : "invoice";

            var draft = new MaxioSubscriptionDraft
            {
                ProductHandle = canonicalPlanHandle,
                CustomerReference = customerReference,
                PaymentCollectionMethod = collectionMethod
            };

            var token = UniquenessToken.For("subscription", customerReference, canonicalPlanHandle);

            try
            {
                var created = await _client.CreateSubscriptionAsync(draft, token, cancellationToken);
                return new SubscribeResult(created, created: true);
            }
            catch (MaxioApiException ex) when (ex.StatusCode == 409)
            {
                return await ResolveDuplicateSubscriptionAsync(customer.Id, canonicalPlanHandle, draft, cancellationToken);
            }
            catch (MaxioApiException ex) when (ex.StatusCode == 422)
            {
                throw new SubscriptionRejectedException(ex.Message);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string customerReference, string email, CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var (firstName, lastName) = MaxioCustomerName.FromEmail(email);
        var draft = new MaxioCustomerDraft
        {
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            Reference = customerReference
        };

        var token = UniquenessToken.For("customer", customerReference);

        try
        {
            return await _client.CreateCustomerAsync(draft, token, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode is 409 or 422)
        {
            // The reference is unique-enforced by Maxio; a concurrent request may have created the
            // customer first (or the uniqueness token was already consumed). Re-read by reference.
            var after = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (after != null)
            {
                return after;
            }

            throw new MaxioApiException($"Unable to create the Maxio customer for '{customerReference}': {ex.Message}", ex.StatusCode, ex.Errors);
        }
    }

    private async Task<SubscribeResult> ResolveDuplicateSubscriptionAsync(long customerId, string planHandle, MaxioSubscriptionDraft draft, CancellationToken cancellationToken)
    {
        // 409 = Maxio already processed an identical create (its uniqueness-token window is 60 minutes).
        // Re-read the customer's subscriptions: if the winning request left a live subscription to this
        // plan, report it back instead of creating a duplicate.
        var reloaded = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        var winner = reloaded.FirstOrDefault(subscription =>
            SubscriptionStates.IsLive(subscription.State) &&
            subscription.Product?.Handle?.Equals(planHandle, StringComparison.OrdinalIgnoreCase) == true);

        if (winner != null)
        {
            return new SubscribeResult(winner, created: false);
        }

        // Otherwise the token was consumed by an earlier attempt that did not leave a live
        // subscription behind (for example a re-subscribe after cancelling within the token window).
        // Retry once with a fresh token so the shopper can subscribe again.
        var retried = await _client.CreateSubscriptionAsync(draft, Guid.NewGuid().ToString(), cancellationToken);
        return new SubscribeResult(retried, created: true);
    }
}

/// <summary>State classification used to decide whether a shopper is currently subscribed.</summary>
public static class SubscriptionStates
{
    // Live/problem states that represent an ongoing relationship. Terminal/end-of-life states
    // (canceled, expired, trial_ended, failed_to_create, suspended, on_hold) are not "subscribed".
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active",
        "trialing",
        "assessing",
        "pending",
        "awaiting_signup",
        "past_due",
        "soft_failure",
        "unpaid"
    };

    public static bool IsLive(string? state)
    {
        return state != null && LiveStates.Contains(state);
    }
}

/// <summary>Deterministic idempotency tokens (Maxio uniqueness_token) for a logical operation.</summary>
public static class UniquenessToken
{
    public static string For(params string[] parts)
    {
        var input = string.Join('\u001f', parts);
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return new Guid(bytes).ToString();
    }
}

/// <summary>
/// eShopOnWeb does not store a first/last name on its identity user, so a display name is derived
/// from the sign-in email (the same value used as the Maxio customer reference).
/// </summary>
public static class MaxioCustomerName
{
    public static (string First, string Last) FromEmail(string? email)
    {
        email = email?.Trim() ?? string.Empty;
        var at = email.IndexOf('@');
        var local = at > 0 ? email[..at] : email;

        var tokens = local.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);

        var firstName = Title(tokens.Length > 0 ? tokens[0] : "eShop");
        if (tokens.Length >= 2)
        {
            var lastName = Title(string.Join(' ', tokens.Skip(1)));
            return (Truncate(firstName, 50), Truncate(lastName, 50));
        }

        return (Truncate(firstName, 50), "Customer");
    }

    private static string Title(string value)
    {
        value = value.Trim();
        return value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
