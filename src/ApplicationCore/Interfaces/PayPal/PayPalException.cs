using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>
/// Raised when PayPal rejects a request or reports something the caller/operator must act on
/// (for example a hold that can no longer be renewed, or a card challenge that would require a
/// browser). The message is intended to be operator-actionable.
/// </summary>
public class PayPalException : Exception
{
    public PayPalException(string message) : base(message) { }
    public PayPalException(string message, Exception inner) : base(message, inner) { }

    /// <summary>PayPal's issue name, when available (e.g. AUTHORIZATION_EXPIRED).</summary>
    public string? IssueName { get; init; }

    /// <summary>True when PayPal asked for a shopper browser challenge (3-D Secure).</summary>
    public bool RequiresPayerAction { get; init; }
}
