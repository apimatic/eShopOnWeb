using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Raised when a PayPal API call returns an error. Carries PayPal's error model (name, message,
/// per-issue details and the debug id) as described by the specs' <c>error</c> schema, so callers
/// can inspect the specific issue (for example to detect an expired authorization).
/// </summary>
public class PayPalApiException : Exception
{
    public PayPalApiException(HttpStatusCode statusCode, string? name, string message,
        IReadOnlyList<PayPalErrorIssue> issues, string? debugId)
        : base(BuildMessage(name, message, issues, debugId))
    {
        StatusCode = statusCode;
        Name = name;
        Issues = issues;
        DebugId = debugId;
    }

    public HttpStatusCode StatusCode { get; }
    public string? Name { get; }
    public IReadOnlyList<PayPalErrorIssue> Issues { get; }
    public string? DebugId { get; }

    public bool HasIssue(string issue) =>
        Issues.Any(i => string.Equals(i.Issue, issue, StringComparison.OrdinalIgnoreCase));

    private static string BuildMessage(string? name, string message, IReadOnlyList<PayPalErrorIssue> issues, string? debugId)
    {
        var issueText = issues.Count > 0
            ? " Issues: " + string.Join("; ", issues.Select(i => $"{i.Issue}: {i.Description}"))
            : string.Empty;
        var debug = string.IsNullOrEmpty(debugId) ? string.Empty : $" (debug_id: {debugId})";
        return $"PayPal error {name}: {message}.{issueText}{debug}";
    }
}

public record PayPalErrorIssue(string? Issue, string? Description);
