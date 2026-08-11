using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a PayPal REST call returns a non-success response. Carries the HTTP status, PayPal's
/// error <c>name</c>, the per-detail <c>issue</c> codes and the <c>debug_id</c> so failures are
/// actionable. Never contains card data.
/// </summary>
public class PayPalApiException : Exception
{
    public int StatusCode { get; }
    public string? ErrorName { get; }
    public IReadOnlyList<string> Issues { get; }
    public string? DebugId { get; }

    public PayPalApiException(int statusCode, string? errorName, IEnumerable<string>? issues, string? debugId, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorName = errorName;
        Issues = issues?.ToList() ?? new List<string>();
        DebugId = debugId;
    }

    public bool HasIssue(params string[] issueNames) =>
        Issues.Any(i => issueNames.Contains(i, StringComparer.OrdinalIgnoreCase));
}
