using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the billing provider (Maxio) rejects a request or cannot be reached.
/// <see cref="StatusCode"/> lets callers translate the failure to an appropriate HTTP response
/// without PublicApi needing to know anything about the billing provider's error shape.
/// </summary>
public class BillingException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public BillingException(string message, HttpStatusCode statusCode = HttpStatusCode.BadGateway) : base(message)
    {
        StatusCode = statusCode;
    }
}
