using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the messaging provider returns an error for an operation whose success the caller
/// depends on (e.g. disposing of message content). The message is deliberately free of any
/// destination number or provider body text so it is safe to surface and log.
/// Surfaces to callers as HTTP 502 Bad Gateway.
/// </summary>
public class SmsGatewayException : Exception
{
    public SmsGatewayException(string message) : base(message)
    {
    }
}
