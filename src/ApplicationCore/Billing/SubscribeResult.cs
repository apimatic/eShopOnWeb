namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>What actually happened when a shopper asked to subscribe.</summary>
public enum SubscribeOutcome
{
    /// <summary>A new subscription was created at the billing provider.</summary>
    Created = 0,

    /// <summary>
    /// The shopper was already enrolled in the requested plan; the existing subscription is returned
    /// unchanged. This is the double-click / retry path — no second subscription is created.
    /// </summary>
    AlreadySubscribed = 1
}

/// <summary>Outcome of a subscribe request, together with the resulting subscription.</summary>
public class SubscribeResult
{
    public SubscribeResult(SubscribeOutcome outcome, CustomerSubscription subscription)
    {
        Outcome = outcome;
        Subscription = subscription;
    }

    public SubscribeOutcome Outcome { get; }
    public CustomerSubscription Subscription { get; }
}
