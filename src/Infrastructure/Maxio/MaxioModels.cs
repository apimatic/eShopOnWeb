using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A Maxio Advanced Billing customer.
/// Maps to the "Customer" schema in the Maxio OpenAPI spec (subset used by eShopOnWeb).
/// </summary>
public sealed class MaxioCustomer
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
}

/// <summary>
/// A Maxio product (subscription plan). Maps to the "Product" schema in the Maxio OpenAPI spec.
/// </summary>
public sealed class MaxioProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public int? TrialInterval { get; set; }
    public string? TrialIntervalUnit { get; set; }
    public long? TrialPriceInCents { get; set; }
    public bool RequireCreditCard { get; set; }
    public bool Taxable { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public string? ProductPricePointName { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

public sealed class MaxioProductFamily
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
}

/// <summary>
/// A Maxio subscription. Maps to the "Subscription" schema in the Maxio OpenAPI spec.
/// </summary>
public sealed class MaxioSubscription
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public long BalanceInCents { get; set; }
    public long ProductPriceInCents { get; set; }
    public string? Reference { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public MaxioCustomer? Customer { get; set; }
    public MaxioProduct? Product { get; set; }
}

/// <summary>
/// A subscription plan offered by eShopOnWeb (a Maxio product in the configured product family).
/// </summary>
public sealed class SubscriptionPlan
{
    public required string Handle { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required long PriceInCents { get; init; }
    public string Price { get; init; } = string.Empty;
    public required int Interval { get; init; }
    public string? IntervalUnit { get; init; }
    public int? TrialDays { get; init; }
    public bool RequiresPaymentMethod { get; init; }
    public bool IsActive { get; init; }
}

/// <summary>
/// Result of a subscribe operation (idempotent — reflects the single subscription
/// that exists for the user + plan, regardless of how many times it was requested).
/// </summary>
public sealed class SubscriptionResult
{
    public required int MaxioSubscriptionId { get; init; }
    public required int MaxioCustomerId { get; init; }
    public required string ProductHandle { get; init; }
    public required string ProductName { get; init; }
    public required long PriceInCents { get; init; }
    public required string State { get; init; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    /// <summary>True when the subscription already existed (idempotent replay).</summary>
    public bool WasExisting { get; set; }
}