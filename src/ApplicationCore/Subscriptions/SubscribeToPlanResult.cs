namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of an enrolment attempt. <see cref="AlreadySubscribed"/> lets the caller distinguish a
/// freshly created subscription from an idempotent replay (a double-clicked Subscribe button).
/// </summary>
public class SubscribeToPlanResult
{
    public SubscribeToPlanResult(CustomerSubscription subscription, bool alreadySubscribed, bool customerCreated)
    {
        Subscription = subscription;
        AlreadySubscribed = alreadySubscribed;
        CustomerCreated = customerCreated;
    }

    public CustomerSubscription Subscription { get; }

    /// <summary>True when a live subscription for this plan already existed and was returned as-is.</summary>
    public bool AlreadySubscribed { get; }

    /// <summary>True when this call created the billing customer; false when an existing one was reused.</summary>
    public bool CustomerCreated { get; }
}
