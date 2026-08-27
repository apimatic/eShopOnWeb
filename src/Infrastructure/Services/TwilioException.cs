using System;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Raised when the Twilio API rejects a request. The message is sanitized:
/// it never contains destination phone numbers or credentials.
/// </summary>
public class TwilioException : Exception
{
    public TwilioException(string message) : base(message)
    {
    }

    public TwilioException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
