namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to. Projected from a billing-provider product
/// (Maxio <c>Product</c> schema) and intentionally keyed by <see cref="Handle"/> rather than by
/// numeric id: handles are the stable identifier across catalog re-seeds.
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
        string? intervalUnit,
        bool requiresPaymentMethod,
        string? productFamilyHandle,
        string? productFamilyName,
        long? trialPriceInCents,
        int? trialInterval,
        string? trialIntervalUnit,
        int? expirationInterval,
        string? expirationIntervalUnit,
        long? initialChargeInCents,
        bool taxable,
        string? pricePointName)
    {
        Handle = handle;
        Name = name;
        Description = description;
        PriceInCents = priceInCents;
        Currency = currency;
        Interval = interval;
        IntervalUnit = intervalUnit;
        RequiresPaymentMethod = requiresPaymentMethod;
        ProductFamilyHandle = productFamilyHandle;
        ProductFamilyName = productFamilyName;
        TrialPriceInCents = trialPriceInCents;
        TrialInterval = trialInterval;
        TrialIntervalUnit = trialIntervalUnit;
        ExpirationInterval = expirationInterval;
        ExpirationIntervalUnit = expirationIntervalUnit;
        InitialChargeInCents = initialChargeInCents;
        Taxable = taxable;
        PricePointName = pricePointName;
    }

    /// <summary>Stable API handle of the plan, e.g. <c>eshop-pro</c>. This is the subscribe target.</summary>
    public string Handle { get; }

    public string Name { get; }

    public string? Description { get; }

    public long PriceInCents { get; }

    /// <summary>ISO currency code the plan bills in, taken from the billing site configuration.</summary>
    public string? Currency { get; }

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period, e.g. <c>1</c>.</summary>
    public int Interval { get; }

    /// <summary>Billing period unit, <c>month</c> or <c>day</c>.</summary>
    public string? IntervalUnit { get; }

    /// <summary>True when the provider requires a payment profile before a subscription can be created.</summary>
    public bool RequiresPaymentMethod { get; }

    public string? ProductFamilyHandle { get; }

    public string? ProductFamilyName { get; }

    public long? TrialPriceInCents { get; }

    public int? TrialInterval { get; }

    public string? TrialIntervalUnit { get; }

    public int? ExpirationInterval { get; }

    public string? ExpirationIntervalUnit { get; }

    public long? InitialChargeInCents { get; }

    public bool Taxable { get; }

    public string? PricePointName { get; }

    public decimal Price => decimal.Divide(PriceInCents, 100m);

    public bool HasTrial => TrialInterval is > 0;
}
