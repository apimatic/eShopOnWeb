using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The PayPal API was unreachable or returned an unexpected/transport-level failure that is
/// not the caller's fault. Surfaced to the caller as a 502 (bad upstream). Carries PayPal's
/// debug id when available so the failure can be traced, but never any card data.
/// </summary>
public class PayPalApiException : Exception
{
    public string? DebugId { get; }

    public PayPalApiException(string message, string? debugId = null) : base(message)
    {
        DebugId = debugId;
    }

    public PayPalApiException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
