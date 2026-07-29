using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a Maxio Advanced Billing operation fails. Carries the upstream HTTP
/// status code and any error messages Maxio returned so callers can surface a
/// meaningful response.
/// </summary>
public class MaxioBillingException : Exception
{
    public MaxioBillingException(string message, int? statusCode = null, IReadOnlyList<string>? errors = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
    }

    /// <summary>Upstream Maxio HTTP status code, when the failure came from an API call.</summary>
    public int? StatusCode { get; }

    /// <summary>Human-readable error messages returned by Maxio, if any.</summary>
    public IReadOnlyList<string> Errors { get; }
}
