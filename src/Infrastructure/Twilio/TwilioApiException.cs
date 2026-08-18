using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Raised when a Twilio API call returns a non-success status. Carries the provider's status code and
/// (where present) its error model fields. The message deliberately omits any PII such as phone numbers.
/// </summary>
public class TwilioApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public int? ProviderCode { get; }

    public TwilioApiException(HttpStatusCode statusCode, int? providerCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ProviderCode = providerCode;
    }
}
