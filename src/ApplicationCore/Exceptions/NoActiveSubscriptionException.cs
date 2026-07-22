using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an operation needs a live subscription and the customer has none (UC2).
/// </summary>
public class NoActiveSubscriptionException : Exception
{
    public NoActiveSubscriptionException(string customerReference)
        : base($"No active subscription found for {customerReference}")
    {
        CustomerReference = customerReference;
    }

    public string CustomerReference { get; }
}
