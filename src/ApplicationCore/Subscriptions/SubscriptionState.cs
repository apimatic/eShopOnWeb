namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Lifecycle state of a subscription, mirroring the <c>Subscription-State</c> schema of the
/// Maxio Advanced Billing OpenAPI specification (maxio-spec/components/schemas/Subscription-State.yaml).
/// </summary>
public enum SubscriptionState
{
    /// <summary>The billing provider reported a state this application does not know about yet.</summary>
    Unknown = 0,
    Pending,
    FailedToCreate,
    Trialing,
    Assessing,
    Active,
    SoftFailure,
    PastDue,
    Suspended,
    Canceled,
    Expired,
    Paused,
    Unpaid,
    TrialEnded,
    OnHold
}

public static class SubscriptionStateExtensions
{
    /// <summary>
    /// True when the subscription still represents an entitlement the shopper holds. States listed by
    /// the specification as "End of Life" (canceled, expired, failed_to_create, on_hold, suspended,
    /// trial_ended) are not live; live and problem states are. An unrecognised state is treated as
    /// live on purpose: it is always safer to surface an existing subscription than to bill twice.
    /// </summary>
    public static bool IsLive(this SubscriptionState state) => state switch
    {
        SubscriptionState.Canceled => false,
        SubscriptionState.Expired => false,
        SubscriptionState.FailedToCreate => false,
        SubscriptionState.OnHold => false,
        SubscriptionState.Suspended => false,
        SubscriptionState.TrialEnded => false,
        SubscriptionState.Active => true,
        SubscriptionState.Assessing => true,
        SubscriptionState.Pending => true,
        SubscriptionState.Trialing => true,
        SubscriptionState.Paused => true,
        SubscriptionState.PastDue => true,
        SubscriptionState.SoftFailure => true,
        SubscriptionState.Unpaid => true,
        _ => true
    };
}
