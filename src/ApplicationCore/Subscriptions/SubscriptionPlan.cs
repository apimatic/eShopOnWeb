namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to, as offered by the billing system of record.
/// </summary>
/// <param name="Handle">Stable, human-readable identifier. Plans are always addressed by handle — never by
/// the provider's numeric id, which is reassigned whenever the catalog is re-seeded.</param>
/// <param name="PriceInCents">Recurring price in minor currency units, exactly as the provider reports it.
/// The conversion to <see cref="Price"/> is done here and nowhere else.</param>
/// <param name="RequiresPaymentProfileAtSignup">True when the provider requires a stored payment profile to
/// be entered in order to sign up for this plan. eShopOnWeb's subscribe flow captures no card, so a plan
/// that reports true would fail at subscribe time; it is surfaced so the drift is visible as data rather
/// than as an opaque rejection. Note this is narrower than it sounds: false does <em>not</em> mean no
/// balance is collected at signup — that is decided by the payment collection method.</param>
public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    string? Currency,
    int? Interval,
    string? IntervalUnit,
    bool RequiresPaymentProfileAtSignup,
    string? ProductFamilyHandle)
{
    /// <summary>The recurring price in major currency units.</summary>
    public decimal Price => PriceInCents / 100m;
}
