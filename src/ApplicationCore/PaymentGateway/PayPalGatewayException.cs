using System;

namespace Microsoft.eShopWeb.ApplicationCore.PaymentGateway;

/// <summary>
/// The single failure type the gateway raises. It carries just enough of PayPal's own error identity
/// (name, fine-grained issue, correlation <c>debug_id</c>, transport status) for callers to branch and
/// for operators to act — never any card data or raw SDK types.
/// </summary>
public class PayPalGatewayException : Exception
{
    public PayPalGatewayException(string message, Exception? inner = null) : base(message, inner)
    {
    }

    /// <summary>HTTP status of the provider response, when one was received.</summary>
    public int? StatusCode { get; init; }

    /// <summary>PayPal's error <c>name</c> (e.g. UNPROCESSABLE_ENTITY), when available.</summary>
    public string? ErrorName { get; init; }

    /// <summary>PayPal's fine-grained issue code (e.g. AUTHORIZATION_EXPIRED), when available.</summary>
    public string? Issue { get; init; }

    /// <summary>PayPal's correlation id for support/log lookup.</summary>
    public string? DebugId { get; init; }

    /// <summary>
    /// True when the authorized payment has gone stale and must be renewed (reauthorized) before a
    /// capture can succeed.
    /// </summary>
    public bool IsAuthorizationExpired { get; init; }

    /// <summary>
    /// True when PayPal answered a card payment with a browser-approval challenge (3DS /
    /// PAYER_ACTION_REQUIRED). This integration does not build an approval round-trip — it surfaces this.
    /// </summary>
    public bool IsPayerActionRequired { get; init; }
}
