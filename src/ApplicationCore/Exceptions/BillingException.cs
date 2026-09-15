using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Application-level billing failure with an HTTP status to surface to API clients.
/// </summary>
public class BillingException : Exception
{
    public BillingException(string message, HttpStatusCode statusCode = HttpStatusCode.BadGateway)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public BillingException(string message, Exception innerException, HttpStatusCode statusCode = HttpStatusCode.BadGateway)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
