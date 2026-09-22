using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the billing integration surfaces. It carries an optional provider HTTP
/// status so the API boundary can distinguish a caller-fixable rejection (4xx) from a provider/transport
/// problem (mapped to 5xx). <see cref="IsCallerError"/> marks failures the caller can act on (e.g. an
/// unknown plan handle) even when no HTTP status is available.
/// </summary>
public class BillingProviderException : Exception
{
    public HttpStatusCode? StatusCode { get; }

    /// <summary>True when the failure is the caller's to fix (bad input) rather than a provider/transport fault.</summary>
    public bool IsCallerError { get; }

    public BillingProviderException(string message, HttpStatusCode? statusCode = null, bool isCallerError = false, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        IsCallerError = isCallerError;
    }
}
