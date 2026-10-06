using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.MaxioBilling;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Typed client for the Maxio Advanced Billing HTTP API (the billing system of record).
/// Only operations verified against Maxio's live API are exposed here.
/// </summary>
public interface IMaxioBillingClient
{
    /// <summary>
    /// Lists active sellable plans (products) belonging to the configured product family.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a Maxio customer by its external reference, or null when none exists.
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a Maxio customer.
    /// </summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerCreate customer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all subscriptions belonging to a Maxio customer.
    /// Note: the sandbox's global ?reference= filter on /subscriptions.json proved
    /// unreliable, so subscription lookup is done per-customer with exact client-side
    /// reference matching — this filter is verified to be correct.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsByCustomerAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls an existing Maxio customer in a plan by product handle.
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionCreate subscription, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a single subscription by Maxio id, or null when not found.
    /// </summary>
    Task<MaxioSubscription?> GetSubscriptionAsync(long subscriptionId, CancellationToken cancellationToken = default);
}