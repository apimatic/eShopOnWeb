using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a call to PayPal fails. Carries PayPal's HTTP status, the machine-readable issue name
/// (when present), and the debug id, so failures can be surfaced and traced with PayPal support.
/// </summary>
public class PayPalGatewayException : Exception
{
    public int StatusCode { get; }
    public string? Issue { get; }
    public string? DebugId { get; }

    public PayPalGatewayException(string message, int statusCode, string? issue = null, string? debugId = null)
        : base(message)
    {
        StatusCode = statusCode;
        Issue = issue;
        DebugId = debugId;
    }
}
