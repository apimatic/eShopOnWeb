using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Twilio;

/// <summary>
/// The single failure type that leaves the Twilio boundary. Carries the provider's HTTP
/// status (when one was received) and a caller-safe message only — never raw provider
/// bodies, which can embed destination numbers.
/// </summary>
public class TwilioProviderException : Exception
{
    public TwilioProviderException(HttpStatusCode? statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode? StatusCode { get; }

    public int? ProviderErrorCode { get; init; }
}
