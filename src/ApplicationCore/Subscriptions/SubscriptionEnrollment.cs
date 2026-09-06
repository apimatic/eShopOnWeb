namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of an enrollment request. <see cref="AlreadySubscribed"/> distinguishes a newly created
/// subscription from one that already existed, which is what makes a repeated POST safe.
/// </summary>
public class SubscriptionEnrollment
{
    public SubscriptionEnrollment(CustomerSubscription subscription, bool alreadySubscribed)
    {
        Subscription = subscription;
        AlreadySubscribed = alreadySubscribed;
    }

    public CustomerSubscription Subscription { get; }

    /// <summary>True when this request found an existing subscription instead of creating one.</summary>
    public bool AlreadySubscribed { get; }
}
