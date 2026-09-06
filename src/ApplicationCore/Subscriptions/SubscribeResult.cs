namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public enum SubscribeOutcome
{
    /// <summary>This request created a new subscription in the billing system.</summary>
    Created = 0,

    /// <summary>
    /// The subscriber was already enrolled - either an equivalent request had already been
    /// processed, or a live subscription to the same plan already existed. No duplicate was made.
    /// </summary>
    AlreadySubscribed = 1,
}

public record SubscribeResult(SubscribeOutcome Outcome, CustomerSubscription Subscription);
