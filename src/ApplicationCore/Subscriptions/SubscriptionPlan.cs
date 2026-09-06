namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to. Plans live in the billing system of record
/// (they are not part of the eShopOnWeb catalog), so the stable identifier is the plan
/// <see cref="Handle"/> - never a provider-assigned numeric id, which can change.
/// </summary>
public sealed record SubscriptionPlan
{
    public required string Handle { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }

    /// <summary>Recurring price, in <see cref="Currency"/> units (not minor units).</summary>
    public required decimal Price { get; init; }

    /// <summary>ISO 4217 currency code the plan is billed in.</summary>
    public required string Currency { get; init; }

    public required BillingInterval Interval { get; init; }

    /// <summary>True when the provider requires a stored payment method before signup.</summary>
    public required bool RequiresPaymentMethod { get; init; }

    /// <summary>Handle of the product family the plan belongs to.</summary>
    public required string ProductFamilyHandle { get; init; }

    /// <summary>Human readable trial summary, or null when the plan has no trial.</summary>
    public string? TrialDescription { get; init; }
}
