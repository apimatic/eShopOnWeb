using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a PayPal API call returns a non-success response. Carries PayPal's own error model
/// (name, message, per-issue details and the debug id) as described by the OpenAPI error schema,
/// so callers can react to specific issues (e.g. AUTHORIZATION_EXPIRED, REFUND_AMOUNT_EXCEEDED).
/// </summary>
public class PayPalApiException : Exception
{
    public int StatusCode { get; }
    public string? ErrorName { get; }
    public string? DebugId { get; }
    public IReadOnlyList<PayPalErrorIssue> Issues { get; }

    public PayPalApiException(int statusCode, string? errorName, string message,
        IReadOnlyList<PayPalErrorIssue>? issues, string? debugId)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorName = errorName;
        DebugId = debugId;
        Issues = issues ?? Array.Empty<PayPalErrorIssue>();
    }

    public bool HasIssue(string issue) =>
        Issues.Any(i => string.Equals(i.Issue, issue, StringComparison.OrdinalIgnoreCase));

    public string DescribeIssues()
    {
        if (Issues.Count == 0)
        {
            return Message;
        }
        return string.Join("; ", Issues.Select(i => $"{i.Issue}: {i.Description}"));
    }
}

public record PayPalErrorIssue(string? Issue, string? Description);
