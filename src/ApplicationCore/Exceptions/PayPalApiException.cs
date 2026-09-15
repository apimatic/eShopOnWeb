using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a PayPal REST call fails. Carries PayPal's HTTP status, the machine-readable issue
/// names, and the debug id so operators can trace the failure with PayPal support.
/// </summary>
public class PayPalApiException : Exception
{
    public PayPalApiException(string message, int statusCode, string? debugId, IReadOnlyList<string> issues)
        : base(message)
    {
        StatusCode = statusCode;
        DebugId = debugId;
        Issues = issues;
    }

    public int StatusCode { get; }
    public string? DebugId { get; }
    public IReadOnlyList<string> Issues { get; }

    public bool HasIssue(params string[] names)
        => Issues.Any(i => names.Any(n => string.Equals(i, n, StringComparison.OrdinalIgnoreCase)));
}
