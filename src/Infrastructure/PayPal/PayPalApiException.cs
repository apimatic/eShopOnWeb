using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// A PayPal call returned an error, parsed from the spec's <c>error</c> model
/// (name / message / debug_id / details[]). Carries the issue codes so callers can react
/// (e.g. detect an expired authorization) and so operators get an actionable message.
/// Derives from <see cref="PaymentException"/> so orchestration can treat gateway failures
/// as domain payment failures.
/// </summary>
public class PayPalApiException : PaymentException
{
    public HttpStatusCode StatusCode { get; }
    public string? PayPalName { get; }
    public string? DebugId { get; }
    public IReadOnlyList<string> Issues { get; }

    public PayPalApiException(HttpStatusCode statusCode, string? name, string message, string? debugId,
        IEnumerable<string>? issues)
        : base(BuildMessage(name, message, debugId, issues))
    {
        StatusCode = statusCode;
        PayPalName = name;
        DebugId = debugId;
        Issues = issues?.ToList() ?? new List<string>();
    }

    private static string BuildMessage(string? name, string message, string? debugId, IEnumerable<string>? issues)
    {
        var issueText = issues is null ? string.Empty : string.Join(", ", issues);
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(name)) parts.Add(name!);
        if (!string.IsNullOrEmpty(message)) parts.Add(message);
        if (!string.IsNullOrEmpty(issueText)) parts.Add($"issues: {issueText}");
        if (!string.IsNullOrEmpty(debugId)) parts.Add($"debug_id: {debugId}");
        return string.Join(" | ", parts);
    }
}
