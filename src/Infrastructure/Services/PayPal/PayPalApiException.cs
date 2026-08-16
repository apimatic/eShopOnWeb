using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// A non-success response from the PayPal REST API. Carries PayPal's error name, the per-detail
/// issue codes and the <c>debug_id</c> (required when contacting PayPal support).
/// </summary>
public class PayPalApiException : Exception
{
    public PayPalApiException(int statusCode, string? name, string? debugId,
        IReadOnlyList<string> issues, string message)
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
        Issues.Contains(issue, StringComparer.OrdinalIgnoreCase) ||
        string.Equals(Name, issue, StringComparison.OrdinalIgnoreCase);
}
