namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>What a subscribe request actually did at the billing provider.</summary>
public enum SubscribeOutcome
{
    /// <summary>A new subscription was created.</summary>
    Created,

    /// <summary>
    /// No subscription was created because the user already held one for this plan, or because the
    /// request replayed a previously-seen idempotency key.
    /// </summary>
    AlreadySubscribed
}

/// <summary>The outcome of a subscribe request together with the resulting subscription.</summary>
public class SubscribeResult
{
    public SubscribeResult(SubscribeOutcome outcome, CustomerSubscription subscription, BillingCustomer customer)
    {
        Outcome = outcome;
        Subscription = subscription;
        Customer = customer;
    }

    public SubscribeOutcome Outcome { get; }

    public CustomerSubscription Subscription { get; }

    public BillingCustomer Customer { get; }

    public bool IsNew => Outcome == SubscribeOutcome.Created;
}
