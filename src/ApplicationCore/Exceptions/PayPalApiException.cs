using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a call to PayPal returns an error response. Carries the fields from PayPal's error model
/// (name, message, debug_id, per-field issues) so the failure can be surfaced in terms an operator can act on.
/// Maps to HTTP 502 Bad Gateway at the API boundary.
/// </summary>
public class PayPalApiException : Exception
{
    public int HttpStatusCode { get; }
    public string? PayPalErrorName { get; }
    public string? DebugId { get; }
    public IReadOnlyList<string> Issues { get; }

    public PayPalApiException(int httpStatusCode, string? payPalErrorName, string message, string? debugId, IReadOnlyList<string> issues)
        : base(message)
    {
        HttpStatusCode = httpStatusCode;
        PayPalErrorName = payPalErrorName;
        DebugId = debugId;
        Issues = issues;
    }
}
