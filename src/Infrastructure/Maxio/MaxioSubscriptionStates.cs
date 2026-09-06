using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Translates the <c>Subscription-State</c> enum of maxio-spec/openapi.yaml into the domain enum.
/// </summary>
public static class MaxioSubscriptionStates
{
    /// <summary>
    /// Parses a Maxio subscription state. An unrecognised value maps to
    /// <see cref="SubscriptionState.Unknown"/> rather than throwing, so a state Maxio adds after
    /// this build shipped degrades gracefully instead of causing an outage; the raw string is
    /// carried through to the caller either way.
    /// </summary>
    public static SubscriptionState Parse(string? state) => state switch
    {
        "pending" => SubscriptionState.Pending,
        "failed_to_create" => SubscriptionState.FailedToCreate,
        "trialing" => SubscriptionState.Trialing,
        "assessing" => SubscriptionState.Assessing,
        "active" => SubscriptionState.Active,
        "soft_failure" => SubscriptionState.SoftFailure,
        "past_due" => SubscriptionState.PastDue,
        "suspended" => SubscriptionState.Suspended,
        "canceled" => SubscriptionState.Canceled,
        "expired" => SubscriptionState.Expired,
        "paused" => SubscriptionState.Paused,
        "unpaid" => SubscriptionState.Unpaid,
        "trial_ended" => SubscriptionState.TrialEnded,
        "on_hold" => SubscriptionState.OnHold,
        "awaiting_signup" => SubscriptionState.AwaitingSignup,
        _ => SubscriptionState.Unknown
    };
}
