using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class MaxioSubscriptionService : ISubscriptionService
{
    private const string CollectionMethodCacheKey = "Maxio:CardlessCollectionMethod";

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscriberLocks = new();

    private readonly IMaxioBillingClient _billingClient;
    private readonly MaxioOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        IMaxioBillingClient billingClient,
        IOptions<MaxioOptions> options,
        IMemoryCache cache,
        ILogger<MaxioSubscriptionService> logger)
    {
        _billingClient = billingClient;
        _options = options.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<MaxioProductFamily?> GetProductFamilyAsync(CancellationToken cancellationToken)
    {
        return await _billingClient.GetProductFamilyByHandleAsync(_options.ProductFamilyHandle, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken)
    {
        return await _billingClient.ListProductsByFamilyHandleAsync(_options.ProductFamilyHandle, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SubscriptionEnrollment> SubscribeAsync(SubscriberProfile subscriber, string planHandle, CancellationToken cancellationToken)
    {
        if (subscriber is null)
        {
            throw new ArgumentNullException(nameof(subscriber));
        }

        if (string.IsNullOrWhiteSpace(subscriber.Email))
        {
            throw new ArgumentException("The subscriber email is required.", nameof(subscriber));
        }

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A plan handle is required.", nameof(planHandle));
        }

        string reference = subscriber.Email;
        SemaphoreSlim gate = SubscriberLocks.GetOrAdd(reference, _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            MaxioCustomer customer = await EnsureCustomerAsync(subscriber, cancellationToken).ConfigureAwait(false);

            IReadOnlyList<MaxioSubscription> existingSubscriptions =
                await _billingClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken).ConfigureAwait(false);

            MaxioSubscription? liveSubscriptionForSamePlan = existingSubscriptions.FirstOrDefault(
                subscription => IsLiveState(subscription.State)
                    && string.Equals(subscription.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase));

            if (liveSubscriptionForSamePlan is not null)
            {
                _logger.LogInformation(
                    "Reusing existing Billing subscription {SubscriptionId} for customer {CustomerId} on plan {PlanHandle}.",
                    liveSubscriptionForSamePlan.Id, customer.Id, planHandle);
                return new SubscriptionEnrollment(liveSubscriptionForSamePlan, alreadyExisted: true);
            }

            string collectionMethod = await GetCardlessCollectionMethodAsync(cancellationToken).ConfigureAwait(false);
            MaxioSubscription created = await _billingClient.CreateSubscriptionAsync(planHandle, customer.Id, collectionMethod, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Created Billing subscription {SubscriptionId} for customer {CustomerId} on plan {PlanHandle} in state {State}.",
                created.Id, customer.Id, planHandle, created.State);

            return new SubscriptionEnrollment(created, alreadyExisted: false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsAsync(string subscriberReference, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(subscriberReference))
        {
            return Array.Empty<MaxioSubscription>();
        }

        MaxioCustomer? customer = await _billingClient.FindCustomerByReferenceAsync(subscriberReference, cancellationToken).ConfigureAwait(false);
        if (customer is null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        return await _billingClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> GetCardlessCollectionMethodAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(CollectionMethodCacheKey, out string? cached) && !string.IsNullOrWhiteSpace(cached))
        {
            return cached;
        }

        MaxioSite site = await _billingClient.GetSiteAsync(cancellationToken).ConfigureAwait(false);
        string collectionMethod = site.RelationshipInvoicingEnabled ? "remittance" : "invoice";

        _cache.Set(CollectionMethodCacheKey, collectionMethod, TimeSpan.FromHours(1));
        return collectionMethod;
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriberProfile subscriber, CancellationToken cancellationToken)
    {
        string reference = subscriber.Email;

        MaxioCustomer? existing = await _billingClient.FindCustomerByReferenceAsync(reference, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        (string firstName, string lastName) = ResolveNames(subscriber);

        try
        {
            return await _billingClient.CreateCustomerAsync(firstName, lastName, subscriber.Email, reference, cancellationToken).ConfigureAwait(false);
        }
        catch (MaxioApiException ex)
        {
            // A concurrent enrollment on another instance may have created the customer
            // between our lookup and create. Re-check by reference before surfacing the error.
            if (ex.StatusCode == (int)System.Net.HttpStatusCode.UnprocessableEntity)
            {
                MaxioCustomer? retry = await _billingClient.FindCustomerByReferenceAsync(reference, cancellationToken).ConfigureAwait(false);
                if (retry is not null)
                {
                    return retry;
                }
            }

            throw;
        }
    }

    private static (string FirstName, string LastName) ResolveNames(SubscriberProfile subscriber)
    {
        string? firstName = string.IsNullOrWhiteSpace(subscriber.FirstName) ? null : subscriber.FirstName;
        string? lastName = string.IsNullOrWhiteSpace(subscriber.LastName) ? null : subscriber.LastName;

        if (firstName is not null && lastName is not null)
        {
            return (firstName, lastName);
        }

        string email = subscriber.Email;
        int at = email.IndexOf('@');
        string localPart = at > 0 ? email[..at] : email;
        string domain = at >= 0 && at < email.Length - 1 ? email[(at + 1)..] : string.Empty;
        string domainRoot = domain.Split('.')[0];

        firstName ??= TitleCase(localPart);
        lastName ??= TitleCase(string.IsNullOrEmpty(domainRoot) ? "Customer" : domainRoot);

        return (firstName, lastName);
    }

    private static string TitleCase(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return trimmed;
        }

        return char.ToUpperInvariant(trimmed[0]) + trimmed[1..];
    }

    private static bool IsLiveState(string? state)
    {
        return state switch
        {
            "active" or "trialing" or "past_due" or "unpaid" or "soft_failure" or "on_hold" or "awaiting_signup" => true,
            _ => false
        };
    }
}
