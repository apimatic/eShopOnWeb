using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a call to the invoicing provider fails. Carries the provider's HTTP status when one was
/// returned (null for a transport failure or an unreadable body) so the boundary can map it back to a
/// caller-facing status deliberately. The message is caller-safe; provider detail is logged, not thrown.
/// </summary>
public class InvoicingProviderException : Exception
{
    public InvoicingProviderException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>The HTTP status the provider returned, if any.</summary>
    public HttpStatusCode? StatusCode { get; }
}
