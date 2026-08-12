using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised by the SMS gateway abstraction when the messaging provider could not be reached
/// or returned an error on an operation whose failure the caller must be told about
/// (validation, redaction). It never carries provider secrets — only a caller-safe message.
///
/// Note: this is deliberately NOT thrown for ordinary order-notification sends. A send that
/// fails must never fail the underlying order operation, so those paths swallow provider
/// errors and record them as a notification outcome instead.
/// </summary>
public class SmsGatewayException : Exception
{
    public SmsGatewayException(string message) : base(message)
    {
    }

    public SmsGatewayException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
