using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of <see cref="ISubscriptionService.SubscribeAsync"/>.
/// </summary>
public class SubscribeResult
{
    private SubscribeResult(CustomerSubscription subscription, bool created, bool customerCreated)
    {
        Subscription = Guard.Against.Null(subscription, nameof(subscription));
        Created = created;
        CustomerCreated = customerCreated;
    }

    public CustomerSubscription Subscription { get; }

    /// <summary>False when an equivalent subscription already existed and was returned instead.</summary>
    public bool Created { get; }

    /// <summary>True when this call also created the provider customer for the user.</summary>
    public bool CustomerCreated { get; }

    public static SubscribeResult NewlyCreated(CustomerSubscription subscription, bool customerCreated) =>
        new(subscription, created: true, customerCreated);

    public static SubscribeResult AlreadySubscribed(CustomerSubscription subscription) =>
        new(subscription, created: false, customerCreated: false);
}
