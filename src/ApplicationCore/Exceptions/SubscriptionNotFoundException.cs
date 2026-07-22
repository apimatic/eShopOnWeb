using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an operation needs a subscription the caller does not have.
/// </summary>
public class SubscriptionNotFoundException : Exception
{
    public SubscriptionNotFoundException(string userReference)
        : base($"No subscription was found for {userReference}.")
    {
    }

    public SubscriptionNotFoundException(int providerSubscriptionId)
        : base($"No subscription was found with id {providerSubscriptionId}.")
    {
    }
}
