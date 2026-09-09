using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A subscribable plan (an Advanced Billing product within the configured
/// product family) offered to shoppers.
/// </summary>
public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    int PriceInCents,
    string Currency,
    string IntervalUnit,
    int Interval);

/// <summary>
/// Current status of a user's subscription, as reported by Maxio.
/// </summary>
public record SubscriptionStatus(
    int MaxioSubscriptionId,
    int MaxioCustomerId,
    string ProductHandle,
    string ProductName,
    int PriceInCents,
    string Currency,
    string State,
    string? NextBillingDateUtc,
    string? CreatedAtUtc);

/// <summary>
/// Port to the Maxio Advanced Billing API (the billing system of record).
/// Implemented in Infrastructure; consumed by the subscription service.
/// All shapes verified against the Maxio Advanced Billing API (sandbox site).
/// </summary>
public interface IMaxioBillingClient
{
    /// <summary>
    /// Lists active, non-archived products belonging to the given product family handle.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a customer by the merchant-side reference (used to store the eShopOnWeb userId).
    /// Returns null when no customer matches.
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a customer. The reference field makes repeat lookups idempotent.
    /// </summary>
    Task<MaxioCustomer> CreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a subscription for a customer on a product (by handle) without
    /// requiring card capture (remittance/invoice collection method).
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(int maxioCustomerId, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a single subscription; returns null if it does not exist.
    /// </summary>
    Task<MaxioSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
}

public record MaxioProduct(
    int Id,
    string Handle,
    string Name,
    string? Description,
    int PriceInCents,
    string IntervalUnit,
    int Interval,
    string ProductFamilyHandle);

public record MaxioCustomer(
    int Id,
    string? Reference,
    string Email,
    string? FirstName,
    string? LastName);

public record MaxioSubscription(
    int Id,
    string State,
    int CustomerId,
    string ProductHandle,
    string ProductName,
    int ProductPriceInCents,
    string Currency,
    string? NextAssessmentAtUtc,
    string? CreatedAtUtc);
