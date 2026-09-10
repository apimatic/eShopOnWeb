using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a PayPal API call returns an error. Carries the fields from PayPal's error model
/// (name, message, debug_id, details[].issue) so callers can act on them.
/// </summary>
public class PayPalGatewayException : Exception
{
    public PayPalGatewayException(int statusCode, string? name, string message, string? debugId,
        IReadOnlyList<string> issues)
        : base(message)
    {
        StatusCode = statusCode;
        Name = name;
        DebugId = debugId;
        Issues = issues;
    }

    public int StatusCode { get; }
    public string? Name { get; }
    public string? DebugId { get; }
    public IReadOnlyList<string> Issues { get; }

    public bool HasIssue(string issue) =>
        Issues.Contains(issue, StringComparer.OrdinalIgnoreCase);
}
