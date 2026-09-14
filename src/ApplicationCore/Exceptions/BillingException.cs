using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the external billing system rejects a request or is unreachable. Carries the
/// upstream HTTP status code (when there is one) so callers can translate it into an
/// appropriate response.
/// </summary>
public class BillingException : Exception
{
    public BillingException(string message, int? statusCode = null, IReadOnlyCollection<string>? errors = null)
        : base(BuildMessage(message, errors))
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
    }

    public BillingException(string message, Exception innerException, int? statusCode = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Errors = Array.Empty<string>();
    }

    /// <summary>The HTTP status code returned by the billing system, if the failure was an HTTP response.</summary>
    public int? StatusCode { get; }

    /// <summary>Any granular error messages returned by the billing system.</summary>
    public IReadOnlyCollection<string> Errors { get; }

    private static string BuildMessage(string message, IReadOnlyCollection<string>? errors)
        => errors is { Count: > 0 } ? $"{message}: {string.Join("; ", errors)}" : message;
}
