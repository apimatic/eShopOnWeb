using MaxioAdvancedBilling.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Classifies Maxio subscription states for this application. The provider supplies the state list; which
/// states entitle a shopper, and which mean "there is already a subscription here", are product decisions.
/// </summary>
internal static class MaxioSubscriptionStates
{
    /// <summary>
    /// States that entitle the shopper to the plan right now. <c>assessing</c> and <c>pending</c> are
    /// deliberately excluded: the provider documents them as transient internal states that access
    /// decisions must not be based on.
    /// </summary>
    public static bool IsEntitling(SubscriptionState? state) =>
        state is not null && (state == SubscriptionState.Active || state == SubscriptionState.Trialing);

    /// <summary>
    /// States in which a subscription no longer occupies the shopper's slot on a plan, so enrolling again
    /// is a genuinely new subscription rather than a duplicate.
    /// </summary>
    public static bool IsTerminal(SubscriptionState? state) =>
        state is not null
        && (state == SubscriptionState.Canceled
            || state == SubscriptionState.Expired
            || state == SubscriptionState.FailedToCreate);

    /// <summary>
    /// True when a subscription counts as already existing for idempotency purposes. Anything that is not
    /// terminal blocks a second enrollment - including the transient states, and including a state this
    /// SDK build does not recognise, because creating a duplicate is worse than reporting an odd one.
    /// </summary>
    public static bool OccupiesPlan(SubscriptionState? state) => !IsTerminal(state);
}
