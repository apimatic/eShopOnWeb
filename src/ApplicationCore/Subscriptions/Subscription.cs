using System;
using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription as reported by Maxio (the system of record).
/// </summary>
public class Subscription
{
    public long Id { get; set; }

    /// <summary>Maxio lifecycle state, e.g. "active", "trialing", "canceled".</summary>
    public string State { get; set; } = string.Empty;

    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;

    public int PriceInCents { get; set; }
    public string Currency { get; set; } = "USD";
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>
    /// When the current billing period ends — i.e. the next billing date for an
    /// active subscription (Maxio: current_period_ends_at / next_assessment_at).
    /// </summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public string PaymentCollectionMethod { get; set; } = string.Empty;

    /// <summary>
    /// True when this subscription was already present before the current request
    /// (i.e. a duplicate subscribe was made idempotent rather than enrolling twice).
    /// </summary>
    public bool AlreadyExisted { get; set; }

    public string FormattedPrice =>
        (PriceInCents / 100m).ToString("C2", CultureInfo.GetCultureInfo("en-US"));
}
