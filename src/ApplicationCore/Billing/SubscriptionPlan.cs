namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A recurring plan a shopper can subscribe to, projected from the billing provider's catalog.
/// </summary>
public class SubscriptionPlan
{
    public SubscriptionPlan(string handle,
        string name,
        string? description,
        long priceInCents,
        int intervalLength,
        string intervalUnit,
        bool? requiresPaymentMethod)
    {
        Handle = handle;
        Name = name;
        Description = description;
        PriceInCents = priceInCents;
        IntervalLength = intervalLength;
        IntervalUnit = intervalUnit;
        RequiresPaymentMethod = requiresPaymentMethod;
    }

    /// <summary>
    /// Stable public identifier of the plan. Numeric provider ids are re-assigned when the
    /// catalog is re-seeded, so the handle is the only durable way to name a plan.
    /// </summary>
    public string Handle { get; }

    public string Name { get; }

    public string? Description { get; }

    /// <summary>
    /// Recurring price in minor units, exactly as the provider reports it.
    /// </summary>
    public long PriceInCents { get; }

    /// <summary>
    /// Recurring price in major units, derived from <see cref="PriceInCents"/>.
    /// </summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>
    /// Number of <see cref="IntervalUnit"/>s in one billing period (for example 1 with "month").
    /// </summary>
    public int IntervalLength { get; }

    /// <summary>
    /// Raw billing-period unit as reported by the provider, for example "month" or "day".
    /// </summary>
    public string IntervalUnit { get; }

    /// <summary>
    /// Whether the provider expects a payment profile to be entered to sign up for this plan. A
    /// display hint only, and null when the provider does not state it: it governs whether a profile
    /// must be <em>entered</em>, not whether a balance due at signup can be settled.
    /// </summary>
    public bool? RequiresPaymentMethod { get; }
}
