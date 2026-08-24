using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the billing system of record rejects a call or is unreachable.
/// </summary>
public class BillingIntegrationException : Exception
{
    public BillingIntegrationException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>HTTP status returned by the billing system, when the failure came from a response.</summary>
    public HttpStatusCode? StatusCode { get; }
}
