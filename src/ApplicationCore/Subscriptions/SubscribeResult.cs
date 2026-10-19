namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public enum SubscribeOutcome
{
    /// <summary>A new subscription was created by this request.</summary>
    Created,
    /// <summary>The user already had this subscription; nothing new was created.</summary>
    AlreadySubscribed,
    /// <summary>
    /// The provider may have accepted the request but could not confirm it yet. Nothing will be
    /// created twice: the next request for this user settles the outcome first.
    /// </summary>
    PendingConfirmation
}

public record SubscribeResult(SubscribeOutcome Outcome, SubscriptionDetails? Subscription);
