namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to, as published by the billing provider.
/// <para>
/// Plans are identified by their <see cref="Handle"/>. Provider-side numeric ids are deliberately
/// not part of this model: they are reassigned whenever the catalog is re-seeded, whereas handles
/// are stable and are what the rest of the integration keys off.
/// </para>
/// </summary>
public class SubscriptionPlan
{
    public SubscriptionPlan(
        string handle,
        string name,
        string? description,
        long priceInCents,
        string? currency,
        int interval,
        string intervalUnit,
        string productFamilyHandle,
        bool requiresPaymentMethod,
        int? trialInterval,
        string? trialIntervalUnit)
    {
        Handle = handle;
        Name = name;
        Description = description;
        PriceInCents = priceInCents;
        Currency = currency;
        Interval = interval;
        IntervalUnit = intervalUnit;
        ProductFamilyHandle = productFamilyHandle;
        RequiresPaymentMethod = requiresPaymentMethod;
        TrialInterval = trialInterval;
        TrialIntervalUnit = trialIntervalUnit;
    }

    /// <summary>Stable API handle of the plan, e.g. <c>eshop-pro</c>. This is the subscribe target.</summary>
    public string Handle { get; }

    public string Name { get; }

    public string? Description { get; }

    /// <summary>Recurring price in the smallest currency unit, as reported by the provider.</summary>
    public long PriceInCents { get; }

    /// <summary>Recurring price as a decimal amount, derived from <see cref="PriceInCents"/>.</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>ISO currency code of the billing site, when known.</summary>
    public string? Currency { get; }

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period, e.g. <c>1</c>.</summary>
    public int Interval { get; }

    /// <summary>Unit of the billing period, e.g. <c>month</c>.</summary>
    public string IntervalUnit { get; }

    public string ProductFamilyHandle { get; }

    /// <summary>True when the provider requires a stored payment method before the plan can be sold.</summary>
    public bool RequiresPaymentMethod { get; }

    public int? TrialInterval { get; }

    public string? TrialIntervalUnit { get; }

    public bool HasTrial => TrialInterval is > 0;
}
