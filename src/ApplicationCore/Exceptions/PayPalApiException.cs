using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A call to the PayPal API failed. Carries PayPal's error name, debug id and issues
/// so operators can correlate with PayPal support without ever seeing card data.
/// </summary>
public class PayPalApiException : Exception
{
    public PayPalApiException(int statusCode, string? errorName, string? debugId, string message, IReadOnlyList<string>? issues = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorName = errorName;
        DebugId = debugId;
        Issues = issues ?? Array.Empty<string>();
    }

    public int StatusCode { get; }
    public string? ErrorName { get; }
    public string? DebugId { get; }
    public IReadOnlyList<string> Issues { get; }

    public bool HasIssue(string issue)
    {
        foreach (var i in Issues)
        {
            if (string.Equals(i, issue, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
