using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when PayPal rejects or fails a request. Carries the HTTP status and PayPal's debug id so
/// the failure can be surfaced to an operator without leaking card data.
/// </summary>
public class PayPalGatewayException : Exception
{
    public int? StatusCode { get; }
    public string? DebugId { get; }

    public PayPalGatewayException(string message, int? statusCode = null, string? debugId = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        DebugId = debugId;
    }
}
