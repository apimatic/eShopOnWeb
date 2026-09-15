using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a resend is requested for a notification that already reached the shopper, so there is
/// nothing to re-send. Surfaced to the caller as a 409.
/// </summary>
public class NotificationNotResendableException : Exception
{
    public NotificationNotResendableException(string message) : base(message)
    {
    }
}
