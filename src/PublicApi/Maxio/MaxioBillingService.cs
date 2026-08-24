using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Orchestrates the subscribe flow against Maxio: idempotent customer provisioning
/// (keyed on the eShopOnWeb user id as the Maxio customer reference) and
/// duplicate-safe subscription creation.
/// </summary>
public class MaxioBillingService
{
    // Subscriptions in these states no longer bill; anything else is considered "live"
    // and blocks creating a duplicate subscription to the same plan.
    private static readonly HashSet<string> EndOfLifeStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "on_hold", "suspended", "trial_ended", "unpaid"
    };

    private readonly IMaxioClient _maxio;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(IMaxioClient maxio, ILogger<MaxioBillingService> logger)
    {
        _maxio = maxio;
        _logger = logger;
    }

    public static bool IsLive(MaxioSubscription subscription) => !EndOfLifeStates.Contains(subscription.State);

    /// <summary>
    /// Returns the Maxio customer for the given eShopOnWeb user, creating it on first use.
    /// Safe against double-clicks and races: the customer reference is unique in Maxio,
    /// so a concurrent create surfaces as a 422 and is resolved by re-reading the lookup.
    /// </summary>
    public async Task<MaxioCustomer> EnsureCustomerAsync(string userReference, string email, CancellationToken cancellationToken = default)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(userReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var localPart = email.Split('@')[0];
        var attributes = new MaxioCustomerAttributes
        {
            FirstName = string.IsNullOrWhiteSpace(localPart) ? "eShop" : localPart,
            LastName = "Customer",
            Email = email,
            Reference = userReference,
            Organization = "eShopOnWeb"
        };

        try
        {
            return await _maxio.CreateCustomerAsync(attributes, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Lost a race with a concurrent create — the customer now exists; fetch it.
            _logger.LogInformation("Customer create for reference {Reference} raced; re-reading lookup.", userReference);
            var winner = await _maxio.FindCustomerByReferenceAsync(userReference, cancellationToken);
            if (winner is not null)
            {
                return winner;
            }

            throw;
        }
    }

    /// <summary>
    /// Subscribes the customer to a plan. If a live subscription to the same plan already
    /// exists it is returned instead of creating a duplicate.
    /// </summary>
    public async Task<(MaxioSubscription Subscription, bool AlreadyExisted)> SubscribeAsync(
        MaxioCustomer customer, string productHandle, CancellationToken cancellationToken = default)
    {
        var existing = await FindLiveSubscriptionAsync(customer.Id, productHandle, cancellationToken);
        if (existing is not null)
        {
            return (existing, true);
        }

        // Deterministic token: a retried/double-submitted POST for the same customer+plan
        // is rejected by Maxio duplicate prevention (409) instead of creating a second subscription.
        var uniquenessToken = $"eshop-{customer.Reference}-{productHandle}";

        try
        {
            var created = await _maxio.CreateSubscriptionAsync(productHandle, customer.Reference!, uniquenessToken, cancellationToken);
            return (created, false);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            _logger.LogInformation("Duplicate subscription submission for {Reference}/{ProductHandle}; re-reading state.",
                customer.Reference, productHandle);
            var winner = await FindLiveSubscriptionAsync(customer.Id, productHandle, cancellationToken);
            if (winner is not null)
            {
                return (winner, true);
            }

            // The earlier attempt did not produce a live subscription; retry once with a fresh token.
            var created = await _maxio.CreateSubscriptionAsync(
                productHandle, customer.Reference!, Guid.NewGuid().ToString(), cancellationToken);
            return (created, false);
        }
    }

    public async Task<MaxioSubscription?> FindLiveSubscriptionAsync(long customerId, string productHandle, CancellationToken cancellationToken = default)
    {
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            IsLive(s) && string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
    }
}
