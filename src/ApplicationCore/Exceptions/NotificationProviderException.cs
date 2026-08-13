using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the SMS notification integration raises when the messaging provider
/// cannot satisfy a request (an API error, a transport failure, or an unreadable response). The
/// message it carries is always caller-safe and never contains a shopper's number or a raw
/// provider body.
/// </summary>
public class NotificationProviderException : Exception
{
    public NotificationProviderException(string message) : base(message)
    {
    }

    public NotificationProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
