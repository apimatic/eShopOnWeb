namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to, as offered by the billing provider.
/// Plans are identified by their stable <see cref="Handle"/>; provider-side numeric ids are deliberately
/// not exposed, because they are not stable across catalog re-seeds.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Stable plan identifier, e.g. <c>eshop-pro</c>. This is what you subscribe with.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in minor units (cents).</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price in major units, derived from <see cref="PriceInCents"/>.</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>ISO currency code of the billing site, e.g. <c>USD</c>.</summary>
    public string? Currency { get; set; }

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period.</summary>
    public int Interval { get; set; }

    /// <summary>Billing period unit as reported by the provider, e.g. <c>month</c> or <c>day</c>.</summary>
    public string? IntervalUnit { get; set; }

    /// <summary>One-off setup fee in minor units; <c>0</c> when the plan has none.</summary>
    public long SetupFeeInCents { get; set; }

    public decimal SetupFee => SetupFeeInCents / 100m;

    /// <summary>Length of the trial in <see cref="TrialIntervalUnit"/>s; <c>0</c> when there is no trial.</summary>
    public int TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }

    public long TrialPriceInCents { get; set; }

    public bool HasTrial => TrialInterval > 0;

    /// <summary>
    /// The provider's own flag for whether its hosted signup page demands a payment profile.
    /// </summary>
    /// <remarks>
    /// Informational only. It governs the provider's signup form, not how a subscription created through
    /// this API collects its balance — that is decided by the collection method the integration sends.
    /// </remarks>
    public bool RequiresPaymentProfileAtSignup { get; set; }
}
