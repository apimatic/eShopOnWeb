using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised by the SMS provider seam when the provider could not carry out a request. Its message is
/// deliberately sanitized — it never carries the destination number — so it is safe to log and to
/// store as a notification's failure reason.
/// </summary>
public class SmsProviderException : Exception
{
    public SmsProviderException(string message) : base(message)
    {
    }

    public SmsProviderException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
