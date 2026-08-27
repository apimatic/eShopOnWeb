using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public enum PaymentFailureKind
{
    /// <summary>The provider declined the payment.</summary>
    Declined,
    /// <summary>PayPal requires a browser-based shopper approval (3DS); this integration does not support it.</summary>
    PayerActionRequired,
    /// <summary>The provider rejected the request as invalid (caller can fix and retry).</summary>
    Validation,
    /// <summary>The referenced provider resource no longer exists (e.g. unknown vault token).</summary>
    NotFound,
    /// <summary>The operation conflicts with the provider-side state (e.g. capture on a voided authorization).</summary>
    Conflict,
    /// <summary>An authorization can no longer be renewed; the shopper must authorize again.</summary>
    AuthorizationNotRenewable,
    /// <summary>The provider could not be reached or did not answer in time; outcome unknown.</summary>
    Unavailable,
    /// <summary>Anything else, including an unreadable provider response.</summary>
    Unexpected
}

/// <summary>
/// The single failure type crossing the payment-gateway boundary. Carries a
/// caller-safe message plus PayPal's own error name/issue verbatim so operators
/// can act on it. Never carries card data or raw exception text.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(PaymentFailureKind kind, string message,
        string? providerErrorName = null, string? providerIssue = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        ProviderErrorName = providerErrorName;
        ProviderIssue = providerIssue;
    }

    public PaymentFailureKind Kind { get; }
    public string? ProviderErrorName { get; }
    public string? ProviderIssue { get; }
}
