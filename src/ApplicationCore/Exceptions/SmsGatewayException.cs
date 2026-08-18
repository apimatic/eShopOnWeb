using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure talking to the SMS provider (an API error or a transport failure). Carries the provider's
/// HTTP status when there was one, so the boundary can map it back deliberately. The message is always
/// caller-safe — it never carries a phone number, a credential, or a raw provider payload.
/// </summary>
public class SmsGatewayException : Exception
{
    /// <summary>The provider's HTTP status code, when the provider answered; null for a transport failure.</summary>
    public int? StatusCode { get; }

    public SmsGatewayException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
