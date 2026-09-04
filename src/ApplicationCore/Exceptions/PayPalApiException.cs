using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// PayPal answered with a non-success response. Carries the PayPal error name, its
/// HTTP status and the individual issue codes so callers can react (e.g. reauthorize an
/// expired authorization).
/// </summary>
public class PayPalApiException : Exception
{
    public string ErrorName { get; }
    public HttpStatusCode StatusCode { get; }
    public IReadOnlyList<string> Issues { get; }

    public PayPalApiException(string errorName, string message, HttpStatusCode statusCode, IReadOnlyList<string> issues)
        : base(message)
    {
        ErrorName = errorName;
        StatusCode = statusCode;
        Issues = issues;
    }

    public bool HasIssue(string issue) => Issues.Any(i => string.Equals(i, issue, StringComparison.OrdinalIgnoreCase));
}