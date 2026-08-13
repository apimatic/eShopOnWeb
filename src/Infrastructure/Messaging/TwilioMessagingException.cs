using System;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Raised when a Twilio API call does not succeed. Messages are deliberately free of any phone
/// number or secret so they are safe to log — they carry only the operation, HTTP status and
/// Twilio error code.
/// </summary>
public class TwilioMessagingException : Exception
{
    public TwilioMessagingException(string message) : base(message) { }
}
