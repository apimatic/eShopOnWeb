namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The outcome of a subscribe request, distinguishing a newly created subscription from one that
/// already existed - so a repeated (double-clicked) request is answered honestly rather than
/// reported as a fresh enrollment.
/// </summary>
public class SubscribeResult
{
    public SubscribeResult(CustomerSubscription subscription, bool alreadySubscribed)
    {
        Subscription = subscription;
        AlreadySubscribed = alreadySubscribed;
    }

    public CustomerSubscription Subscription { get; }

    /// <summary>True when this request found an existing live subscription instead of creating one.</summary>
    public bool AlreadySubscribed { get; }
}
