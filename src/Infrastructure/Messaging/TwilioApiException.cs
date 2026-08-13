using System;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Raised when a Twilio API call fails. The message deliberately carries only the HTTP status and
/// Twilio's numeric error code — never the provider's raw error text, which can echo the destination
/// number, and never a shopper's number or the auth token.
/// </summary>
public class TwilioApiException : Exception
{
    public int StatusCode { get; }
    public int? TwilioCode { get; }

    public TwilioApiException(int statusCode, int? twilioCode, string operation)
        : base($"Twilio {operation} failed with HTTP {statusCode}" + (twilioCode.HasValue ? $" (code {twilioCode})." : "."))
    {
        StatusCode = statusCode;
        TwilioCode = twilioCode;
    }
}
