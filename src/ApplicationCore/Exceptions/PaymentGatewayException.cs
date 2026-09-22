using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment-processor failure surfaced to the application with a caller-safe message. Carries the
/// provider's own error name and correlation id (when available) for diagnostics, and the HTTP status
/// where the transport produced one. Never wraps raw SDK/JSON exception text meant for callers.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, string? providerCode = null,
        string? providerDebugId = null, int? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        ProviderCode = providerCode;
        ProviderDebugId = providerDebugId;
        StatusCode = statusCode;
    }

    /// <summary>PayPal's error <c>name</c> (e.g. INSTRUMENT_DECLINED), when the body carried one.</summary>
    public string? ProviderCode { get; }

    /// <summary>PayPal's <c>debug_id</c> correlation id, when present.</summary>
    public string? ProviderDebugId { get; }

    /// <summary>HTTP status from the transport, when one was produced.</summary>
    public int? StatusCode { get; }
}
