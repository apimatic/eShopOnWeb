using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The subscription is not in a state that can accrue usage. Rejected before any provider call
/// (plan.md UC2, "the customer has no active subscription" failure scenario).
/// </summary>
public class NoActiveSubscriptionException : Exception
{
    public NoActiveSubscriptionException(string message) : base(message)
    {
    }

    public static NoActiveSubscriptionException ForUser(string userName) =>
        new($"User '{userName}' has no active subscription to record usage against.");

    public static NoActiveSubscriptionException ForSubscription(int subscriptionId, string state) =>
        new($"Subscription {subscriptionId} is {state} and cannot accrue usage.");
}
