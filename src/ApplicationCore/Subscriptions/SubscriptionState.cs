namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Lifecycle bucket for a subscription. The billing provider exposes a much richer state
/// machine; <see cref="Subscription.ProviderState"/> carries the verbatim provider value while
/// this enum is what the storefront makes access decisions on.
/// </summary>
public enum SubscriptionState
{
    /// <summary>Provider reported a state this build does not recognise.</summary>
    Unknown = 0,

    /// <summary>Signup is still settling (pending / assessing / awaiting signup).</summary>
    Pending = 1,

    /// <summary>Entitlements should be granted (active or trialing).</summary>
    Active = 2,

    /// <summary>Live, but payment needs attention (past due, unpaid, soft failure...).</summary>
    ProblemState = 3,

    /// <summary>End of life - canceled, expired, on hold, failed to create.</summary>
    Ended = 4
}
