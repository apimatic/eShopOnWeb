namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to. Projected from the billing provider's catalog;
/// eShopOnWeb never owns plan pricing.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Stable, human readable identifier of the plan. This is what callers subscribe to.</summary>
    public required string Handle { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Recurring price, in the minor unit of <see cref="Currency"/>.</summary>
    public required long PriceInCents { get; init; }

    /// <summary>ISO 4217 currency code of the billing site, e.g. "USD".</summary>
    public required string Currency { get; init; }

    public required BillingInterval Interval { get; init; }

    /// <summary>Handle of the product family (catalog) the plan belongs to.</summary>
    public string? ProductFamilyHandle { get; init; }

    /// <summary>True when the billing provider will refuse the signup unless a payment method is captured first.</summary>
    public bool RequiresPaymentMethod { get; init; }

    /// <summary>Length of the free trial in <see cref="TrialIntervalUnit"/>s, when the plan has one.</summary>
    public int? TrialInterval { get; init; }

    public string? TrialIntervalUnit { get; init; }

    public bool HasTrial => TrialInterval is > 0;
}
