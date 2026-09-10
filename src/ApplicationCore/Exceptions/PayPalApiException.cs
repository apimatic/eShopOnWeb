using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a PayPal REST call returns an error. Carries the spec's error model
/// (name / message / debug_id / issues) so callers can act on it rather than only logging it.
/// </summary>
public class PayPalApiException : Exception
{
    public PayPalApiException(int httpStatusCode, string? name, string message, string? debugId, IReadOnlyList<string> issues)
        : base(message)
    {
        HttpStatusCode = httpStatusCode;
        Name = name;
        DebugId = debugId;
        Issues = issues;
    }

    public int HttpStatusCode { get; }
    public string? Name { get; }
    public string? DebugId { get; }
    public IReadOnlyList<string> Issues { get; }
}
