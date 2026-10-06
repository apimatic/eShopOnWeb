namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// Outcome of a subscribe call: the subscription in the billing system, and
/// whether it was already enrolled (idempotent replay) or freshly created.
/// </summary>
public class SubscriptionProvisioningResult
{
    public SubscriptionProvisioningResult(MaxioSubscription subscription, bool alreadySubscribed)
    {
        Subscription = subscription;
        AlreadySubscribed = alreadySubscribed;
    }

    public MaxioSubscription Subscription { get; }

    public bool AlreadySubscribed { get; }
}
