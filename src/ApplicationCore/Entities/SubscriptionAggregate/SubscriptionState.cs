namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The provider-agnostic lifecycle state of a subscription. The billing provider reports a richer
/// set of raw states; <see cref="Subscription.ProviderState"/> preserves the verbatim value.
/// </summary>
public enum SubscriptionState
{
    /// <summary>The provider reported a state this application does not model.</summary>
    Unknown = 0,

    /// <summary>The subscription is being created and has not settled yet.</summary>
    Pending,

    /// <summary>The subscription is live and billing normally (includes trialing).</summary>
    Active,

    /// <summary>The subscription is live but payment has failed or is outstanding.</summary>
    PastDue,

    /// <summary>Billing has been temporarily suspended and the subscription can be resumed.</summary>
    Paused,

    /// <summary>The subscription has been cancelled and can be reactivated.</summary>
    Cancelled,

    /// <summary>The subscription ran its full life cycle and cannot be resumed.</summary>
    Expired
}
