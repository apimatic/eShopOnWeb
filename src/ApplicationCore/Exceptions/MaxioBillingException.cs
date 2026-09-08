using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the billing integration surfaces. Its <see cref="Message"/> is always
/// caller-safe (no provider/SDK internals leak) and <see cref="StatusCode"/> carries the HTTP status
/// the API boundary should return: a provider 4xx the caller can act on maps to that same 4xx, while
/// a transport failure or an unreadable success body maps to 5xx.
/// </summary>
public class MaxioBillingException : Exception
{
    public MaxioBillingException(string message, int statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>Client-facing HTTP status code (e.g. 400, 404, 409, 422, 502, 503).</summary>
    public int StatusCode { get; }
}
