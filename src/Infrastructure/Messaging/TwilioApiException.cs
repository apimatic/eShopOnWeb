using System;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>A non-success response from the Twilio API. Its message is redacted of anything phone-number-like.</summary>
public class TwilioApiException : Exception
{
    public TwilioApiException(string message) : base(message)
    {
    }
}
