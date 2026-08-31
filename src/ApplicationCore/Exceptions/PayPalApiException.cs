using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A call to the PayPal API failed. Carries PayPal's error name, message, debug id and
/// fine-grained issues so callers can react (e.g. renew an expired authorization).
/// Never carries card data.
/// </summary>
public class PayPalApiException : Exception
{
    public PayPalApiException(HttpStatusCode statusCode, string? errorName, string message, string? debugId, IReadOnlyList<string>? issues = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorName = errorName;
        DebugId = debugId;
        Issues = issues ?? new List<string>();
    }

    public HttpStatusCode StatusCode { get; }
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
