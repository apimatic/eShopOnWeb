using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Application service that manages eShopOnWeb shoppers' Maxio Advanced Billing
/// subscriptions. Maxio is the system of record: no subscription state is
/// persisted locally, which keeps the integration correct even when the local
/// store is ephemeral (e.g. the in-memory EF provider used on dev machines).
///
/// Idempotency:
/// * A Maxio customer is keyed by a deterministic, opaque reference derived from
///   the authenticated eShop user, so "ensure customer" never duplicates one --
///   including across application restarts and under a double-click race
///   (a duplicate create is answered by Maxio with 422 and is then re-read).
/// * Subscribing is serialized per user in-process and re-checks the user's
///   existing live subscriptions before creating, so a double-click can never
///   produce two subscriptions to the same plan.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Lists the subscription plans (Maxio products) available in the configured product family.</summary>
    Task<IReadOnlyList<MaxioProductDto>> ListAvailablePlansAsync(CancellationToken cancellationToken);

    /// <summary>Returns the site's currency code (e.g. "USD").</summary>
    Task<string> GetSiteCurrencyAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Ensures a Maxio customer exists for <paramref name="user"/> and subscribes them to the plan
    /// identified by <paramref name="productHandle"/>. When the user already has a live subscription
    /// to that plan the existing subscription is returned unchanged (<see cref="SubscriptionEnrollmentResult.Created"/> = false).
    /// </summary>
    Task<SubscriptionEnrollmentResult> EnsureSubscriptionAsync(
        string userEmail,
        string productHandle,
        CancellationToken cancellationToken);

    /// <summary>Lists the Maxio subscriptions belonging to <paramref name="user"/> (empty when never enrolled).</summary>
    Task<IReadOnlyList<MaxioSubscriptionDto>> ListUserSubscriptionsAsync(
        string userEmail,
        CancellationToken cancellationToken);
}

/// <summary>Result of an idempotent subscribe operation.</summary>
public sealed record SubscriptionEnrollmentResult(MaxioSubscriptionDto Subscription, bool Created);

public sealed class MaxioSubscriptionService : ISubscriptionService
{
    // Live states for which an existing subscription is treated as the user's current entitlement.
    private static readonly HashSet<string> EntitledStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active",
        "trialing",
    };

    private const string PaymentCollectionMethodRemittance = "remittance";

    // Per-user in-process locks that serialize customer/subscription creation for the same shopper.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new(StringComparer.Ordinal);

    private readonly MaxioClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(MaxioClient client, IOptions<MaxioOptions> options, ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProductDto>> ListAvailablePlansAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        return await _client.ListProductsByFamilyHandleAsync(_options.ProductFamilyHandle!, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<string> GetSiteCurrencyAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var site = await _client.GetSiteAsync(cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(site.Currency) ? "USD" : site.Currency;
    }

    public async Task<SubscriptionEnrollmentResult> EnsureSubscriptionAsync(
        string userEmail,
        string productHandle,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(userEmail))
        {
            throw new ArgumentException("A valid user email is required.", nameof(userEmail));
        }

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new ArgumentException("A product (plan) handle is required.", nameof(productHandle));
        }

        var key = BuildCustomerReference(userEmail);
        var gate = UserLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var customer = await EnsureCustomerAsync(userEmail, cancellationToken).ConfigureAwait(false);

            var existing = await FindLiveSubscriptionAsync(customer.Id, productHandle, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "User {Email} already has {State} subscription {SubscriptionId} to plan {ProductHandle}; returning it.",
                    userEmail, existing.State, existing.Id, productHandle);
                return new SubscriptionEnrollmentResult(existing, Created: false);
            }

            var request = new MaxioCreateSubscriptionRequest(new MaxioSubscriptionUpsert
            {
                ProductHandle = productHandle,
                CustomerId = customer.Id,
                PaymentCollectionMethod = PaymentCollectionMethodRemittance,
            });

            try
            {
                var created = await _client.CreateSubscriptionAsync(request, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation(
                    "Created Maxio subscription {SubscriptionId} ({State}) for user {Email} on plan {ProductHandle}.",
                    created.Id, created.State, userEmail, productHandle);
                return new SubscriptionEnrollmentResult(created, Created: true);
            }
            catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                // A concurrent request may have won the race between our list check and the create.
                var raced = await FindLiveSubscriptionAsync(customer.Id, productHandle, cancellationToken).ConfigureAwait(false);
                if (raced is not null)
                {
                    _logger.LogInformation(
                        "Subscription create for user {Email} on plan {ProductHandle} raced; returning {SubscriptionId}.",
                        userEmail, productHandle, raced.Id);
                    return new SubscriptionEnrollmentResult(raced, Created: false);
                }
                throw;
            }
        }
        finally
        {
            gate.Release();
            if (gate.CurrentCount == 1)
            {
                UserLocks.TryRemove(key, out _);
            }
        }
    }

    public async Task<IReadOnlyList<MaxioSubscriptionDto>> ListUserSubscriptionsAsync(
        string userEmail,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var customer = await FindCustomerAsync(userEmail, cancellationToken).ConfigureAwait(false);
        if (customer is null)
        {
            return Array.Empty<MaxioSubscriptionDto>();
        }

        return await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<MaxioCustomerDto> EnsureCustomerAsync(string userEmail, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerAsync(userEmail, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var reference = BuildCustomerReference(userEmail);
        var (firstName, lastName) = SplitDisplayName(userEmail);

        try
        {
            return await _client.CreateCustomerAsync(
                new MaxioCreateCustomerRequest(new MaxioCustomerUpsert
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = userEmail,
                    Reference = reference,
                }),
                cancellationToken).ConfigureAwait(false);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // "Reference must be unique" -- another request just created this customer. Re-read.
            var raced = await FindCustomerAsync(userEmail, cancellationToken).ConfigureAwait(false);
            if (raced is not null)
            {
                return raced;
            }
            throw;
        }
    }

    private Task<MaxioCustomerDto?> FindCustomerAsync(string userEmail, CancellationToken cancellationToken)
    {
        var reference = BuildCustomerReference(userEmail);
        return _client.FindCustomerByReferenceAsync(reference, cancellationToken);
    }

    private async Task<MaxioSubscriptionDto?> FindLiveSubscriptionAsync(
        long customerId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken).ConfigureAwait(false);
        foreach (var subscription in subscriptions)
        {
            var isSamePlan = string.Equals(
                subscription.Product?.Handle,
                productHandle,
                StringComparison.OrdinalIgnoreCase);
            if (isSamePlan && subscription.State is not null && EntitledStates.Contains(subscription.State))
            {
                return subscription;
            }
        }

        return null;
    }

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw new MaxioUnavailableException(
                "Maxio Advanced Billing is not configured. Set Maxio:ApiKey, Maxio:Subdomain and " +
                "Maxio:ProductFamilyHandle (via the MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN and " +
                "MAXIO_DEFAULT_PRODUCT_FAMILY environment variables or .NET user-secrets).");
        }
    }

    /// <summary>
    /// Builds a stable, opaque Maxio customer reference for an eShop user. It is derived
    /// from the normalized email address, so the same shopper always maps to the same
    /// Maxio customer -- across restarts and regardless of ephemeral local storage --
    /// without embedding personal data in the reference.
    /// </summary>
    internal static string BuildCustomerReference(string userEmail)
    {
        var normalized = userEmail.Trim().ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"eshop-customer:{normalized}"));
        return "eshop_cust_" + Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }

    internal static (string FirstName, string LastName) SplitDisplayName(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0)
        {
            return ("eShop", "Subscriber");
        }

        var local = email[..at];
        var domain = email[(at + 1)..];
        return (local, string.IsNullOrWhiteSpace(domain) ? "Subscriber" : domain);
    }
}
