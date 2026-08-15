using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The single failure type the Maxio billing boundary raises. Every SDK failure — an API error, a
/// transport failure, or an unreadable/drifted response body — is translated into this exception so
/// callers deal with one type. <see cref="StatusCode"/> carries the HTTP status the endpoint should
/// surface: a provider 4xx the caller can act on stays a 4xx; anything unknown surfaces as 5xx.
/// The <see cref="Exception.Message"/> is always caller-safe (never a raw SDK/framework message).
/// </summary>
public class MaxioBillingException : Exception
{
    /// <summary>The HTTP status the API endpoint should return to its caller.</summary>
    public HttpStatusCode StatusCode { get; }

    public MaxioBillingException(string message, HttpStatusCode statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
