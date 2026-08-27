using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A call to PayPal failed. Carries PayPal's error name and issue codes so callers can
/// react (e.g. renew a stale authorization) without parsing messages.
/// </summary>
public class PayPalGatewayException : Exception
{
    public PayPalGatewayException(HttpStatusCode statusCode, string? errorName, string message,
        IReadOnlyList<string>? issues = null, string? debugId = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorName = errorName;
        Issues = issues ?? Array.Empty<string>();
        DebugId = debugId;
    }

    public HttpStatusCode StatusCode { get; }
    public string? ErrorName { get; }
    public IReadOnlyList<string> Issues { get; }
    public string? DebugId { get; }

    public bool HasIssue(string issue) =>
        string.Equals(ErrorName, issue, StringComparison.OrdinalIgnoreCase) ||
        System.Linq.Enumerable.Any(Issues, i => string.Equals(i, issue, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// PayPal answered a card operation with a challenge (e.g. 3-D Secure) that requires the
/// shopper to approve in a browser. This integration is server-to-server only, so this is
/// surfaced as an actionable error rather than an approval round-trip.
/// </summary>
public class PayPalPayerActionRequiredException : Exception
{
    public PayPalPayerActionRequiredException(string? approvalUrl)
        : base("PayPal requires the shopper to approve this payment in a browser (e.g. 3-D Secure), " +
               "which this server-to-server integration does not support.")
    {
        ApprovalUrl = approvalUrl;
    }

    public string? ApprovalUrl { get; }
}
