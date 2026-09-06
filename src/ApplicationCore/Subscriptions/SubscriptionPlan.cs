using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to.
/// </summary>
/// <remarks>
/// Plans are a projection of the billing system's catalog. eShopOnWeb keeps no copy of them:
/// the billing system is the system of record, and <see cref="Handle"/> is the only identifier
/// that is stable across catalog re-seeds.
/// </remarks>
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
        string productFamilyHandle,
        bool requiresPaymentMethod,
        int? trialInterval,
        string? trialIntervalUnit)
    {
        Handle = Guard.Against.NullOrWhiteSpace(handle, nameof(handle));
        Name = Guard.Against.NullOrWhiteSpace(name, nameof(name));
        Description = description;
        PriceInCents = Guard.Against.Negative(priceInCents, nameof(priceInCents));
        Currency = Guard.Against.NullOrWhiteSpace(currency, nameof(currency));
        Interval = interval;
        IntervalUnit = intervalUnit;
        ProductFamilyHandle = productFamilyHandle;
        RequiresPaymentMethod = requiresPaymentMethod;
        TrialInterval = trialInterval;
        TrialIntervalUnit = trialIntervalUnit;
    }

    /// <summary>Stable API handle of the plan, e.g. <c>eshop-pro</c>. Used to subscribe.</summary>
    public string Handle { get; }

    public string Name { get; }

    public string? Description { get; }

    /// <summary>Recurring price in minor units, as held by the billing system.</summary>
    public long PriceInCents { get; }

    /// <summary>ISO-4217 currency of the billing site, e.g. <c>USD</c>.</summary>
    public string Currency { get; }

    /// <summary>Billing period length, expressed in <see cref="IntervalUnit"/>s.</summary>
    public int Interval { get; }

    /// <summary><c>month</c> or <c>day</c>.</summary>
    public string IntervalUnit { get; }

    public string ProductFamilyHandle { get; }

    /// <summary>
    /// True when the billing system refuses a signup without a stored payment profile.
    /// eShopOnWeb does not capture card details, so such plans cannot be subscribed to here.
    /// </summary>
    public bool RequiresPaymentMethod { get; }

    public int? TrialInterval { get; }

    public string? TrialIntervalUnit { get; }

    public bool HasTrial => TrialInterval is > 0;

    public decimal Price => PriceInCents / 100m;
}
