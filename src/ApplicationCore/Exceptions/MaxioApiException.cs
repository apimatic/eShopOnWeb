using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the billing system of record (Maxio Advanced Billing) rejected
/// or failed a request. Carries the upstream status code and error payload so
/// callers can translate the failure appropriately.
/// </summary>
public class MaxioApiException : MaxioBillingException
{
    public MaxioApiException(string message, int upstreamStatusCode, string? upstreamErrors = null, Exception? innerException = null)
        : base(message, innerException)
    {
        UpstreamStatusCode = upstreamStatusCode;
        UpstreamErrors = upstreamErrors;
    }

    /// <summary>
    /// HTTP status code returned by the billing system, when the failure was an HTTP-level rejection.
    /// </summary>
    public int? UpstreamStatusCode { get; }

    /// <summary>
    /// Raw error information returned by the billing system, when available.
    /// </summary>
    public string? UpstreamErrors { get; }
}
