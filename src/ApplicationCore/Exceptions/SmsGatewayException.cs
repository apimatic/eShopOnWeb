using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the SMS provider boundary raises, whatever the underlying cause (a provider
/// error status, an unreachable provider, or a response that could not be processed). Carries the
/// provider HTTP status when one was returned, so the API boundary can map "the caller's fault" apart
/// from "the provider is unavailable". Never carries message content or a phone number.
/// </summary>
public class SmsGatewayException : Exception
{
    public SmsGatewayException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }

    public SmsGatewayException(string message, HttpStatusCode statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>The provider HTTP status, when the provider actually answered with one.</summary>
    public HttpStatusCode? StatusCode { get; }
}
