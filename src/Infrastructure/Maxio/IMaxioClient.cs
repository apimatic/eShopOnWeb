using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Port to the Maxio Advanced Billing API. All operations were verified
/// against the Maxio Advanced Billing (Chargify) HTTP API:
/// Basic authentication with the API key as username and "x" as password,
/// JSON bodies and responses wrapped in a singular resource envelope.
/// </summary>
public interface IMaxioClient
{
    /// <summary>
    /// Finds a customer by its stable reference (the eShopOnWeb user id).
    /// Returns null when no customer exists for the reference.
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken ct = default);

    /// <summary>
    /// Creates a customer. Maxio enforces unique references.
    /// </summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioNewCustomer customer, CancellationToken ct = default);

    /// <summary>
    /// Lists all subscriptions belonging to a customer.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken ct = default);

    /// <summary>
    /// Creates a subscription for a customer on the product with the given handle.
    /// Payment collection uses invoice/remittance so enrollment works without
    /// card capture or 3-DS.
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string productHandle, CancellationToken ct = default);

    /// <summary>
    /// Fetches a single subscription. Returns null when it does not exist.
    /// </summary>
    Task<MaxioSubscription?> GetSubscriptionAsync(long subscriptionId, CancellationToken ct = default);

    /// <summary>
    /// Lists the non-archived products of the configured product family.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListFamilyProductsAsync(CancellationToken ct = default);
}

/// <summary>
/// Data for creating a Maxio customer.
/// </summary>
public class MaxioNewCustomer
{
    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Reference { get; set; } = string.Empty;
}

/// <summary>
/// Maxio customer projection (verified against GET/POST /customers.json).
/// </summary>
public class MaxioCustomer
{
    public long Id { get; set; }

    public string Reference { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;
}

/// <summary>
/// Maxio product family projection.
/// </summary>
public class MaxioProductFamily
{
    public long Id { get; set; }

    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Maxio product (subscription plan) projection (verified against
/// GET /product_families/{id}/products.json).
/// </summary>
public class MaxioProduct
{
    public long Id { get; set; }

    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int PriceInCents { get; set; }

    public int Interval { get; set; }

    public string IntervalUnit { get; set; } = string.Empty;

    public DateTime? ArchivedAt { get; set; }

    public MaxioProductFamily? ProductFamily { get; set; }
}

/// <summary>
/// Maxio subscription projection (verified against POST/GET /subscriptions.json).
/// </summary>
public class MaxioSubscription
{
    public long Id { get; set; }

    public string State { get; set; } = string.Empty;

    public int BalanceInCents { get; set; }

    public int? ProductPriceInCents { get; set; }

    public string? Currency { get; set; }

    /// <summary>When the next billing event is assessed (next_assessment_at).</summary>
    public DateTime? NextAssessmentAt { get; set; }

    public DateTime? ActivatedAt { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? CanceledAt { get; set; }

    public string? PaymentCollectionMethod { get; set; }

    public MaxioProduct? Product { get; set; }

    public MaxioCustomer? Customer { get; set; }
}
