using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Wraps a non-success response from the PayPal API. Carries only PayPal's
/// error metadata (issue/debug id) - never request payloads.
/// </summary>
public class PayPalApiException : Exception
{
    public PayPalApiException(int statusCode, string? errorName, string? issue, string? debugId, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorName = errorName;
        Issue = issue;
        DebugId = debugId;
    }

    public int StatusCode { get; }
    public string? ErrorName { get; }
    public string? Issue { get; }
    public string? DebugId { get; }
}
