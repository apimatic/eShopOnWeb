using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the billing system of record (Maxio) rejects a request or is unreachable.
/// The billing layer translates transport/SDK exceptions into this domain exception so the
/// API surface never leaks SDK types, and carries an <see cref="IsClientError"/> hint so the
/// edge can map it to a 4xx (bad request) versus a 502 (upstream) response.
/// </summary>
public class BillingException : Exception
{
    public BillingException(string message, bool isClientError = false, Exception? innerException = null)
        : base(message, innerException)
    {
        IsClientError = isClientError;
    }

    /// <summary>
    /// True when the failure was caused by the request itself (e.g. validation, 422) rather
    /// than an upstream/transport fault.
    /// </summary>
    public bool IsClientError { get; }
}
