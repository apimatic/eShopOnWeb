using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>How a <see cref="SubscribeCommand"/> was satisfied.</summary>
public enum SubscribeOutcome
{
    /// <summary>A new subscription was created in the billing system by this call.</summary>
    Created = 0,

    /// <summary>The shopper already had a live subscription to the plan; that one was returned.</summary>
    AlreadySubscribed = 1,

    /// <summary>An earlier request carrying the same idempotency key had already created it.</summary>
    IdempotentReplay = 2
}

public class SubscribeResult
{
    public SubscribeResult(CustomerSubscription subscription, SubscribeOutcome outcome)
    {
        Subscription = Guard.Against.Null(subscription, nameof(subscription));
        Outcome = outcome;
    }

    public CustomerSubscription Subscription { get; }

    public SubscribeOutcome Outcome { get; }

    /// <summary>True only when this call is what brought the subscription into existence.</summary>
    public bool Created => Outcome == SubscribeOutcome.Created;
}
