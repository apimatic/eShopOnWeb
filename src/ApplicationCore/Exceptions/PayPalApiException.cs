using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a PayPal REST call returns an error response. Carries the fields PayPal
/// returns (name, issue, and the <c>debug_id</c> that support needs to trace the request)
/// so callers can react to specific conditions (e.g. an expired authorization).
/// </summary>
public class PayPalApiException : Exception
{
    public PayPalApiException(int statusCode, string? name, string? issue, string? debugId, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Name = name;
        Issue = issue;
        DebugId = debugId;
    }

    public int StatusCode { get; }
    public string? Name { get; }
    public string? Issue { get; }
    public string? DebugId { get; }
}
