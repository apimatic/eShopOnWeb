using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure at the billing-provider boundary. <see cref="StatusCode"/> carries the
/// provider's HTTP status for client-actionable failures (4xx); provider outages and
/// unreadable responses surface as 5xx. <see cref="Exception.Message"/> is always
/// caller-safe — provider bodies and framework exception details are logged, not returned.
/// </summary>
public class BillingException : Exception
{
    public BillingException(HttpStatusCode statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
