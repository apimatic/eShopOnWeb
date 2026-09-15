using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a PayPal API call returns an error response. Carries the PayPal error model
/// (name, debug id and per-detail issue codes) modelled on the spec's <c>error</c> schema,
/// so callers can branch on specific issues (for example an expired authorization).
/// </summary>
public class PayPalApiException : Exception
{
    public PayPalApiException(HttpStatusCode statusCode, string? name, string? message, string? debugId,
        IReadOnlyList<string> issues, string? rawBody)
        : base(BuildMessage(statusCode, name, message, issues))
    {
        StatusCode = statusCode;
        Name = name;
        DebugId = debugId;
        Issues = issues;
        RawBody = rawBody;
    }

    public HttpStatusCode StatusCode { get; }
    public string? Name { get; }
    public string? DebugId { get; }
    public IReadOnlyList<string> Issues { get; }
    public string? RawBody { get; }

    public bool HasIssue(string issue) =>
        Issues.Any(i => string.Equals(i, issue, StringComparison.OrdinalIgnoreCase)) ||
        string.Equals(Name, issue, StringComparison.OrdinalIgnoreCase);

    private static string BuildMessage(HttpStatusCode statusCode, string? name, string? message, IReadOnlyList<string> issues)
    {
        var issueText = issues.Count > 0 ? $" [{string.Join(", ", issues)}]" : string.Empty;
        return $"PayPal API error {(int)statusCode} {name}: {message}{issueText}";
    }
}
