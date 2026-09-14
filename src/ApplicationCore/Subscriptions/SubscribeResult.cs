namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe request. <see cref="AlreadyExisted"/> is <c>true</c> when an
/// equivalent live subscription was already present and was returned instead of creating a
/// new one — this is how the subscribe flow stays idempotent against double submissions.
/// </summary>
public class SubscribeResult
{
    public SubscribeResult(CustomerSubscription subscription, bool alreadyExisted)
    {
        Subscription = subscription;
        AlreadyExisted = alreadyExisted;
    }

    /// <summary>The active (existing or newly created) subscription.</summary>
    public CustomerSubscription Subscription { get; }

    /// <summary>True when an existing live subscription was returned rather than a new one created.</summary>
    public bool AlreadyExisted { get; }
}
