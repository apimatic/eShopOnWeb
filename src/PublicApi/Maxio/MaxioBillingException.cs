using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Raised when a Maxio Advanced Billing call fails. Carries the HTTP status the
/// caller should see (provider 4xx passes through; anything else becomes 502) and
/// a caller-safe message that never leaks SDK internals.
/// </summary>
public class MaxioBillingException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public MaxioBillingException(HttpStatusCode statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
