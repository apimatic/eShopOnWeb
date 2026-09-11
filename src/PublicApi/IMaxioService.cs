using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Service for interacting with Maxio Advanced Billing.
/// </summary>
public interface IMaxioService
{
    /// <summary>
    /// Lists subscription plans (products) for the configured product family.
    /// </summary>
    Task<IReadOnlyList<MaxioPlanInfo>> ListPlansAsync();

    /// <summary>
    /// Gets a specific plan by its handle.
    /// </summary>
    Task<MaxioPlanInfo?> GetPlanByHandleAsync(string handle);

    /// <summary>
    /// Finds or creates a Maxio customer for the given user reference (idempotent).
    /// </summary>
    Task<MaxioCustomerInfo> FindOrCreateCustomerAsync(string reference, string email, string firstName, string lastName);

    /// <summary>
    /// Creates a subscription for a customer on a specific plan (by product handle).
    /// Idempotent: if the customer already has an active subscription on this plan, returns the existing one.
    /// </summary>
    Task<MaxioSubscriptionInfo> CreateOrFindSubscriptionAsync(
        int customerId,
        string productHandle,
        string? productPricePointHandle = null);

    /// <summary>
    /// Lists all subscriptions for a given customer.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscriptionInfo>> ListCustomerSubscriptionsAsync(int customerId);

    /// <summary>
    /// Reads a single subscription by its Maxio ID.
    /// </summary>
    Task<MaxioSubscriptionInfo?> ReadSubscriptionAsync(int subscriptionId);
}

public class MaxioPlanInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public int Interval { get; set; }
    public bool RequireCreditCard { get; set; }
    public string? ProductFamilyHandle { get; set; }
    public string? ProductPricePointName { get; set; }
}

public class MaxioCustomerInfo
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
}

public class MaxioSubscriptionInfo
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public int? ProductId { get; set; }
    public string? ProductName { get; set; }
    public string? ProductHandle { get; set; }
    public long? ProductPriceInCents { get; set; }
    public int? CustomerId { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerFirstName { get; set; }
    public string? CustomerLastName { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? CreatedAt { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}
