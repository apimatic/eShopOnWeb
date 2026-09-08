using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Implementation of <see cref="IMaxioSubscriptionService"/> backed by the Maxio Advanced Billing API.
///
/// Maxio is the billing system of record. The eShopOnWeb application user is linked to a Maxio
/// customer through the Maxio customer <c>reference</c> field, which is set to the application
/// user name (stable and unique). Because every interaction is resolved through Maxio by that
/// reference, the mapping survives process restarts and database resets without any local state.
/// </summary>
public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    /// <summary>
    /// New subscriptions are created with remittance (invoice) payment collection. The seeded demo
    /// plans require no payment method, so this avoids attempting a card capture / 3-DS at signup.
    /// </summary>
    private const string PaymentCollectionMethodRemittance = "remittance";

    private static readonly HashSet<string> OccupiedStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active",
        "trialing",
        "trial_ended",
        "awaiting_signup",
        "pending",
        "assessing"
    };

    // Serializes subscribe operations per customer reference so a double-click (or any concurrent
    // subscribe for the same user) can never create two customers or two subscriptions.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> PerReferenceLocks = new();

    private static readonly object CurrencyCacheLock = new();
    private static string? _cachedSiteCurrency;

    private readonly MaxioOptions _options;
    private readonly MaxioApiClient _client;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        IOptions<MaxioOptions> options,
        MaxioApiClient client,
        ILogger<MaxioSubscriptionService> logger)
    {
        _options = options.Value;
        _client = client;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken)
    {
        _options.EnsureConfigured();
        var plans = await _client.ListProductsAsync(_options.ProductFamilyHandle!, cancellationToken);
        return plans
            .Where(p => p.ArchivedAt is null)
            .OrderBy(p => p.PriceInCents ?? long.MaxValue)
            .ToList();
    }

    public async Task<string?> GetSiteCurrencyAsync(CancellationToken cancellationToken)
    {
        var cached = _cachedSiteCurrency;
        if (cached is not null)
        {
            return cached;
        }

        var site = await _client.GetSiteAsync(cancellationToken);
        lock (CurrencyCacheLock)
        {
            _cachedSiteCurrency ??= site?.Currency;
        }

        return _cachedSiteCurrency;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListUserSubscriptionsAsync(string customerReference, CancellationToken cancellationToken)
    {
        _options.EnsureConfigured();
        var customer = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer?.Id is null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        return await _client.ListCustomerSubscriptionsAsync(customer.Id.Value, cancellationToken);
    }

    public async Task<MaxioSubscribeResult> SubscribeAsync(
        string customerReference,
        string email,
        string productHandle,
        CancellationToken cancellationToken)
    {
        _options.EnsureConfigured();

        var gate = PerReferenceLocks.GetOrAdd(customerReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(customerReference, email, cancellationToken);

            var existing = await FindCurrentSubscriptionAsync(customer.Id!.Value, productHandle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation("User {CustomerReference} already has {State} subscription {SubscriptionId} to plan {ProductHandle}.",
                    customerReference, existing.State, existing.Id, productHandle);
                return new MaxioSubscribeResult(existing, AlreadySubscribed: true);
            }

            var created = await _client.CreateSubscriptionAsync(
                customer.Id.Value,
                productHandle,
                PaymentCollectionMethodRemittance,
                cancellationToken);

            _logger.LogInformation("Created Maxio subscription {SubscriptionId} ({State}) for customer {CustomerId} on plan {ProductHandle}.",
                created.Id, created.State, customer.Id, productHandle);

            return new MaxioSubscribeResult(created, AlreadySubscribed: false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string customerReference, string email, CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitName(email);

        MaxioCustomer created;
        try
        {
            created = await _client.CreateCustomerAsync(firstName, lastName, email, customerReference, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict)
        {
            // Another (e.g. multi-instance) create may have won the race. Reconcile against Maxio.
            var after = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (after is not null)
            {
                return after;
            }

            throw;
        }

        _logger.LogInformation("Created Maxio customer {CustomerId} with reference {CustomerReference}.", created.Id, customerReference);
        return created;
    }

    private async Task<MaxioSubscription?> FindCurrentSubscriptionAsync(long customerId, string productHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            OccupiedStates.Contains(s.State ?? string.Empty) &&
            string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
    }

    private static (string FirstName, string LastName) SplitName(string email)
    {
        var at = email.IndexOf('@');
        var firstName = at > 0 ? email.Substring(0, at) : "eShop";
        if (string.IsNullOrWhiteSpace(firstName) || firstName.Length > 40)
        {
            firstName = "eShop";
        }

        return (firstName, "Shopper");
    }
}
