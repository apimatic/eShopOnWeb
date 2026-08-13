using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the SMS gateway surfaces. Infrastructure translates every provider
/// (non-2xx), transport, and unreadable-body failure into this so callers handle one type.
/// <see cref="StatusCode"/> carries the provider's HTTP status when there was one (a genuine
/// provider rejection the caller can act on) and is null for transport/unknown failures.
/// The message is caller-safe and never contains a destination number or a secret.
/// </summary>
public class SmsGatewayException : Exception
{
    /// <summary>The provider's HTTP status, when the failure was a provider response; otherwise null.</summary>
    public HttpStatusCode? StatusCode { get; }

    public SmsGatewayException(string message) : base(message)
    {
    }

    public SmsGatewayException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public SmsGatewayException(string message, HttpStatusCode statusCode, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
