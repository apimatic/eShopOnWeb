// SubscriptionState.cs
//
// Says what a subscription's state means for your app: whether the customer has access, and where
// the subscription stands (live, transient, recoverable or ended). Every state the SDK declares is
// classified here, and this file stops compiling when the SDK gains a state it does not classify.
//
// Implements: knowledge/rules.md R16; knowledge/lifecycles.md L3.
//
// What it does not do:
// - It does not keep your copy of the state current. Re-read the subscription before you act on a
//   webhook (knowledge/rules.md R20, knowledge/decisions.md D19).
// - It does not remember the access decision you already hold. A transient state must not change
//   that decision, so AccessAfter takes it as an argument.
// - It does not filter a list. The `state` filter of ListSubscriptions takes a different set of
//   values, SubscriptionStateFilter. Ten of the fifteen states can be asked for there. soft_failure,
//   pending, assessing, paused and failed_to_create cannot, and ListSubscriptionsRequest.State holds
//   one value. So "everyone with access" is: list with no state filter, then keep the rows whose
//   state HasAccess. For a transient row use the decision you stored for it; with none, count
//   assessing in (a renewal in progress; its entitlements stay on) and pending out
//   (knowledge/lifecycles.md L3).
//
// Maxio's schema calls canceled, expired, failed_to_create, on_hold, suspended and trial_ended
// "End of Life", meaning not billing. Two of them, on_hold and suspended, come back, so they are
// Recoverable here and only the other four are Ended.
//
// Copy into your project and adapt. Not part of the SDK's API.

#nullable enable

using MaxioAdvancedBilling.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Recipes;

/// <summary>Where a subscription stands. Returned by <see cref="SubscriptionStates.Standing"/>.</summary>
public enum SubscriptionStanding
{
    /// <summary>
    /// A state this file does not classify: Maxio sent one the SDK does not declare, or sent none.
    /// Grant nothing new, keep the access decision you hold, and never sign the customer up again
    /// beside it until you know what the state means.
    /// </summary>
    Unclassified = 0,

    /// <summary>active, trialing, past_due or soft_failure. The customer has access.</summary>
    Live,

    /// <summary>
    /// pending or assessing. Maxio is creating or assessing the subscription and does not always
    /// show these states. Keep the access decision you already hold.
    /// </summary>
    Transient,

    /// <summary>
    /// on_hold, suspended, unpaid, awaiting_signup or paused. Not billing, and it comes back. The
    /// customer has no access. Never sign the customer up again beside it.
    /// </summary>
    Recoverable,

    /// <summary>
    /// canceled, expired, trial_ended or failed_to_create. The customer has no access. A new signup
    /// or a reactivation is possible.
    /// </summary>
    Ended,
}

/// <summary>The meaning of each subscription state (knowledge/lifecycles.md L3).</summary>
public static class SubscriptionStates
{
    /// <summary>
    /// Where a subscription in <paramref name="state"/> stands. A null state, and a state the SDK
    /// does not declare, are <see cref="SubscriptionStanding.Unclassified"/>.
    /// </summary>
    public static SubscriptionStanding Standing(this SubscriptionState? state)
    {
        if (state is null)
            return SubscriptionStanding.Unclassified;

        // Match takes one argument per state the SDK declares. When a regenerated SDK gains a
        // state, this call stops compiling until the new state is classified here.
        return state.Match(
            onPending: () => SubscriptionStanding.Transient,
            onFailedToCreate: () => SubscriptionStanding.Ended,
            onTrialing: () => SubscriptionStanding.Live,
            onAssessing: () => SubscriptionStanding.Transient,
            onActive: () => SubscriptionStanding.Live,
            onSoftFailure: () => SubscriptionStanding.Live,
            // Access stays on through dunning.
            onPastDue: () => SubscriptionStanding.Live,
            // A prepayment restores it.
            onSuspended: () => SubscriptionStanding.Recoverable,
            onCanceled: () => SubscriptionStanding.Ended,
            onExpired: () => SubscriptionStanding.Ended,
            // No API call produces paused. It is an internal state: the merchant's own Maxio
            // account is in arrears. A subscription you pause is on_hold.
            onPaused: () => SubscriptionStanding.Recoverable,
            onUnpaid: () => SubscriptionStanding.Recoverable,
            onTrialEnded: () => SubscriptionStanding.Ended,
            // It resumes.
            onOnHold: () => SubscriptionStanding.Recoverable,
            onAwaitingSignup: () => SubscriptionStanding.Recoverable,
            otherwise: _ => SubscriptionStanding.Unclassified);
    }

    /// <summary>
    /// True for active, trialing, past_due and soft_failure, false for every other state. For a
    /// state that is <see cref="SubscriptionStanding.Transient"/> or
    /// <see cref="SubscriptionStanding.Unclassified"/> false does not mean "take access away":
    /// use <see cref="AccessAfter"/> when you already hold a decision.
    /// </summary>
    public static bool HasAccess(this SubscriptionState? state) =>
        state.Standing() == SubscriptionStanding.Live;

    /// <summary>
    /// The access decision to store after you read <paramref name="state"/>, given the decision
    /// you held before. A live state turns access on. A recoverable or ended state turns it off.
    /// A transient or unclassified state leaves <paramref name="accessBefore"/> as it was.
    /// </summary>
    public static bool AccessAfter(this SubscriptionState? state, bool accessBefore)
    {
        switch (state.Standing())
        {
            case SubscriptionStanding.Live:
                return true;
            case SubscriptionStanding.Recoverable:
            case SubscriptionStanding.Ended:
                return false;
            default:
                return accessBefore;
        }
    }
}
