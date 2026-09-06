namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The lifecycle state of a subscription held by the billing provider.
/// </summary>
/// <remarks>
/// Mirrors the <c>Subscription State</c> enumeration defined by the Maxio Advanced Billing
/// OpenAPI specification (<c>maxio-spec/components/schemas/Subscription-State.yaml</c>).
/// <see cref="Unknown"/> is not part of the specification; it is used when the provider reports a
/// state this build does not recognise, so that forward compatibility never costs a caller a
/// failed request.
/// </remarks>
public enum SubscriptionState
{
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
    OnHold,
    AwaitingSignup
}

public static class SubscriptionStateExtensions
{
    /// <summary>
    /// Returns <c>true</c> while the subscription still represents a commitment between the
    /// customer and the merchant, i.e. while subscribing again to the same plan would be a
    /// duplicate rather than a genuine re-subscribe.
    /// </summary>
    /// <remarks>
    /// The specification groups states into "Live", "Problem" and "End of Life" buckets. Live and
    /// Problem states are both treated as occupied here: a past due subscription still exists and
    /// must not be silently duplicated by a retried subscribe request.
    /// </remarks>
    public static bool IsOccupied(this SubscriptionState state) => state switch
    {
        SubscriptionState.Pending => true,
        SubscriptionState.Trialing => true,
        SubscriptionState.Assessing => true,
        SubscriptionState.Active => true,
        SubscriptionState.SoftFailure => true,
        SubscriptionState.PastDue => true,
        SubscriptionState.Suspended => true,
        SubscriptionState.Paused => true,
        SubscriptionState.Unpaid => true,
        SubscriptionState.AwaitingSignup => true,
        _ => false
    };
}
