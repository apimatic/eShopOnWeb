using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the Maxio Advanced Billing integration cannot complete a request.
/// <see cref="StatusCode"/> carries the HTTP status the caller should see: a Maxio-side
/// rejection (4xx) is preserved as the equivalent client status, while a transport failure,
/// an unreadable response, or an unexpected Maxio-side error maps to 502.
/// </summary>
public class MaxioServiceException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public MaxioServiceException(string message, HttpStatusCode statusCode) : base(message)
    {
        StatusCode = statusCode;
    }

    public MaxioServiceException(string message, HttpStatusCode statusCode, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
