using System;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Raised for unexpected, non-recoverable failures talking to PayPal (e.g. authentication failures
/// or malformed responses). Business-level declines are surfaced as gateway results, not exceptions.
/// </summary>
public class PayPalApiException : Exception
{
    public int? StatusCode { get; }
    public string? DebugId { get; }

    public PayPalApiException(string message, int? statusCode = null, string? debugId = null)
        : base(message)
    {
        StatusCode = statusCode;
        DebugId = debugId;
    }
}
