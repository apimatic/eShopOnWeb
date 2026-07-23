using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an operation needs a subscription the user does not have. Thrown before any
/// provider call, so nothing is sent to the billing provider (UC2).
/// </summary>
public class NoActiveSubscriptionException : Exception
{
    public NoActiveSubscriptionException(string userReference)
        : base($"No subscription exists for '{userReference}'.")
    {
        UserReference = userReference;
    }

    public string UserReference { get; }
}
