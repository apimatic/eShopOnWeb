using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when PayPal returns an error response. Carries the parsed PayPal error model (name, issues, debug id)
/// so callers can react to specific issues and operators get an actionable message.
/// </summary>
public class PayPalApiException : Exception
{
    public int StatusCode { get; }
    public string? PayPalName { get; }
    public string? DebugId { get; }
    public IReadOnlyList<string> Issues { get; }

    public PayPalApiException(int statusCode, string? payPalName, string message, IReadOnlyList<string> issues, string? debugId)
        : base(BuildMessage(statusCode, payPalName, message, issues, debugId))
    {
        StatusCode = statusCode;
        PayPalName = payPalName;
        Issues = issues;
        DebugId = debugId;
    }

    /// <summary>True if PayPal reported the given fine-grained issue code anywhere in the error details.</summary>
    public bool HasIssue(string issue) =>
        Issues.Any(i => string.Equals(i, issue, StringComparison.OrdinalIgnoreCase));

    private static string BuildMessage(int statusCode, string? name, string message, IReadOnlyList<string> issues, string? debugId)
    {
        var issuesPart = issues.Count > 0 ? $" Issues: {string.Join(", ", issues)}." : string.Empty;
        var namePart = string.IsNullOrEmpty(name) ? string.Empty : $" {name}:";
        var debugPart = string.IsNullOrEmpty(debugId) ? string.Empty : $" (debug_id: {debugId})";
        return $"PayPal API error ({statusCode}).{namePart} {message}.{issuesPart}{debugPart}".Replace("  ", " ").Trim();
    }
}
