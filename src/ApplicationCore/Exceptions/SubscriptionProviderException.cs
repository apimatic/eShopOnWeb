using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing provider could not be reached, returned an unreadable response, or rejected a
/// request for a reason the caller cannot fix (a 5xx/transport/parse failure, not a validation error).
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
