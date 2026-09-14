using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the Maxio subscription-billing provider is unreachable, rejects a request in a
/// way the caller cannot self-correct, or returns a response that cannot be trusted.
/// </summary>
public class SubscriptionProviderException : Exception
{
    public SubscriptionProviderException(string message) : base(message)
    {
    }

    public SubscriptionProviderException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
