using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>Result of a subscribe attempt.</summary>
public class SubscribeResult
{
    public SubscribeResult(CustomerSubscription subscription, SubscribeOutcome outcome)
    {
        Subscription = Guard.Against.Null(subscription, nameof(subscription));
        Outcome = outcome;
    }

    public CustomerSubscription Subscription { get; }

    public SubscribeOutcome Outcome { get; }

    /// <summary>True only when this call is the one that created the subscription.</summary>
    public bool Created => Outcome == SubscribeOutcome.Created;
}
