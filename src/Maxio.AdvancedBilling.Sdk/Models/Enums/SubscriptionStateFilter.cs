using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

/// <summary>
/// Allowed values for filtering by the current state of the subscription.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<SubscriptionStateFilter>))]
public sealed record SubscriptionStateFilter : StringEnum<SubscriptionStateFilter>
{
    private SubscriptionStateFilter(string value) : base(value)
    {
    }

    public static readonly SubscriptionStateFilter Active = new("active");

    public static readonly SubscriptionStateFilter Canceled = new("canceled");

    public static readonly SubscriptionStateFilter Expired = new("expired");

    public static readonly SubscriptionStateFilter ExpiredCards = new("expired_cards");

    public static readonly SubscriptionStateFilter OnHold = new("on_hold");

    public static readonly SubscriptionStateFilter PastDue = new("past_due");

    public static readonly SubscriptionStateFilter PendingCancellation = new("pending_cancellation");

    public static readonly SubscriptionStateFilter PendingRenewal = new("pending_renewal");

    public static readonly SubscriptionStateFilter Suspended = new("suspended");

    public static readonly SubscriptionStateFilter TrialEnded = new("trial_ended");

    public static readonly SubscriptionStateFilter Trialing = new("trialing");

    public static readonly SubscriptionStateFilter Unpaid = new("unpaid");

    public static SubscriptionStateFilter FromValue(string value) => FromValueCore(value);
}
