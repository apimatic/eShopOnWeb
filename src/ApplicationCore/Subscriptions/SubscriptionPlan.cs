using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to, as published by the billing provider.
/// </summary>
public class SubscriptionPlan
{
    public SubscriptionPlan(string handle, string name, string? description, long priceInCents,
        int intervalLength, string intervalUnit, bool paymentMethodRequired,
        string? pricePointHandle, string? pricePointName,
        int? trialIntervalLength, string? trialIntervalUnit, string? productFamilyHandle)
    {
        Handle = handle;
        Name = name;
        Description = description;
        PriceInCents = priceInCents;
        IntervalLength = intervalLength;
        IntervalUnit = intervalUnit;
        PaymentMethodRequired = paymentMethodRequired;
        PricePointHandle = pricePointHandle;
        PricePointName = pricePointName;
        TrialIntervalLength = trialIntervalLength;
        TrialIntervalUnit = trialIntervalUnit;
        ProductFamilyHandle = productFamilyHandle;
    }

    /// <summary>The stable API handle of the plan. This is the value callers subscribe with.</summary>
    public string Handle { get; }

    public string Name { get; }

    public string? Description { get; }

    /// <summary>Recurring price in minor units (cents). Authoritative; <see cref="Price"/> is derived.</summary>
    public long PriceInCents { get; }

    /// <summary>Recurring price in major units, derived from <see cref="PriceInCents"/>.</summary>
    public decimal Price => decimal.Divide(PriceInCents, 100m);

    /// <summary>Number of <see cref="IntervalUnit"/>s in a billing period (e.g. 1 with "month" = monthly).</summary>
    public int IntervalLength { get; }

    /// <summary>"month" or "day".</summary>
    public string IntervalUnit { get; }

    /// <summary>True when the provider requires a stored payment method before a signup can succeed.</summary>
    public bool PaymentMethodRequired { get; }

    /// <summary>Handle of the price point the plan currently defaults to, when the provider exposes one.</summary>
    public string? PricePointHandle { get; }

    public string? PricePointName { get; }

    public int? TrialIntervalLength { get; }

    public string? TrialIntervalUnit { get; }

    public bool HasTrial => TrialIntervalLength > 0;

    public string? ProductFamilyHandle { get; }
}
