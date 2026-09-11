using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Service interface for interacting with the Maxio Advanced Billing API.
/// </summary>
public interface IMaxioService
{
    /// <summary>
    /// Gets all products (plans) for the configured product family.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> GetProductsAsync();

    /// <summary>
    /// Gets a single product by its handle.
    /// </summary>
    Task<MaxioProduct?> GetProductByHandleAsync(string handle);

    /// <summary>
    /// Looks up a customer by their reference (eShopOnWeb user ID).
    /// Returns null if not found.
    /// </summary>
    Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference);

    /// <summary>
    /// Creates a new customer in Maxio.
    /// </summary>
    Task<MaxioCustomer> CreateCustomerAsync(string reference, string firstName, string lastName, string email);

    /// <summary>
    /// Ensures a Maxio customer exists for the given reference. Idempotent:
    /// returns existing customer if found, creates new one otherwise.
    /// </summary>
    Task<MaxioCustomer> EnsureCustomerAsync(string reference, string firstName, string lastName, string email);

    /// <summary>
    /// Creates a subscription for a customer.
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle);

    /// <summary>
    /// Gets a subscription by its ID.
    /// </summary>
    Task<MaxioSubscription?> GetSubscriptionAsync(int subscriptionId);

    /// <summary>
    /// Lists all subscriptions for a customer by their reference.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsByCustomerReferenceAsync(string customerReference);
}

/// <summary>
/// Represents a Maxio product (plan).
/// </summary>
public class MaxioProduct
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; set; } = string.Empty;

    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; set; }

    [JsonPropertyName("taxable")]
    public bool Taxable { get; set; }

    [JsonPropertyName("product_family")]
    public MaxioProductFamily? ProductFamily { get; set; }
}

/// <summary>
/// Represents a Maxio product family.
/// </summary>
public class MaxioProductFamily
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string Handle { get; set; } = string.Empty;
}

/// <summary>
/// Represents a Maxio customer.
/// </summary>
public class MaxioCustomer
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Represents a Maxio subscription.
/// </summary>
public class MaxioSubscription
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    [JsonPropertyName("product_id")]
    public int ProductId { get; set; }

    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    [JsonPropertyName("product_name")]
    public string? ProductName { get; set; }

    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("current_period_started_at")]
    public DateTime? CurrentPeriodStartsAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTime? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTime? NextAssessmentAt { get; set; }

    [JsonPropertyName("canceled_at")]
    public DateTime? CanceledAt { get; set; }

    [JsonPropertyName("cancel_at_end_of_period")]
    public bool CancelAtEndOfPeriod { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}
