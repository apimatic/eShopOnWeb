using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// An operation that requires a live subscription was requested for a user who does not have one.
/// Raised before any provider call is made, so nothing is billed (UC2 failure scenario: "the
/// customer has no active subscription → reject the usage report; nothing is sent to the provider").
/// </summary>
public class NoActiveSubscriptionException : Exception
{
    public NoActiveSubscriptionException(string userReference)
        : base($"No active subscription found for '{userReference}'. Subscribe to a plan first.")
    {
    }

    public NoActiveSubscriptionException(int subscriptionId, object currentState)
        : base($"Subscription {subscriptionId} is not active (it is {currentState}), so usage cannot be recorded against it.")
    {
    }
}
