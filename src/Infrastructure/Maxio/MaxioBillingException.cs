using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// The single failure type the billing integration raises. It carries a caller-safe message and,
/// where known, the provider HTTP <see cref="StatusCode"/> so the API layer can map a provider 4xx
/// back to a client 4xx and treat transport/parse failures as 5xx — without leaking SDK internals.
/// </summary>
public class MaxioBillingException : Exception
{
    public MaxioBillingException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>Provider HTTP status, when the failure originated from an HTTP error response.</summary>
    public HttpStatusCode? StatusCode { get; }
}
