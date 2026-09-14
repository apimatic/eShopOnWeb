namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to, as published by the billing system of record.
/// </summary>
public class SubscriptionPlan
{
    public SubscriptionPlan(
        string handle,
        string name,
        string? description,
        long priceInCents,
        string currency,
        int interval,
        string intervalUnit,
        long setupFeeInCents,
        int? trialInterval,
        string? trialIntervalUnit,
        long? trialPriceInCents,
        bool requiresPaymentMethod,
        string productFamilyHandle)
    {
        Handle = handle;
        Name = name;
        Description = description;
        PriceInCents = priceInCents;
        Currency = currency;
        Interval = interval;
        IntervalUnit = intervalUnit;
        SetupFeeInCents = setupFeeInCents;
        TrialInterval = trialInterval;
        TrialIntervalUnit = trialIntervalUnit;
        TrialPriceInCents = trialPriceInCents;
        RequiresPaymentMethod = requiresPaymentMethod;
        ProductFamilyHandle = productFamilyHandle;
    }

    /// <summary>Stable API handle. This — never the numeric id — is what callers subscribe by.</summary>
    public string Handle { get; }

    public string Name { get; }

    public string? Description { get; }

    public long PriceInCents { get; }

    public string Currency { get; }

    /// <summary>Billing period length, expressed in <see cref="IntervalUnit"/>s.</summary>
    public int Interval { get; }

    /// <summary>"month" or "day".</summary>
    public string IntervalUnit { get; }

    public long SetupFeeInCents { get; }

    public int? TrialInterval { get; }

    public string? TrialIntervalUnit { get; }

    public long? TrialPriceInCents { get; }

    public bool HasTrial => TrialInterval.GetValueOrDefault() > 0;

    /// <summary>True when the billing system refuses a signup with no payment method on file.</summary>
    public bool RequiresPaymentMethod { get; }

    public string ProductFamilyHandle { get; }
}
