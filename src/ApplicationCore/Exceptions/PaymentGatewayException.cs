using System;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type a payment provider surfaces to the application. The gateway
/// boundary converts SDK/transport failures into this; the API boundary maps Kind to a
/// caller-appropriate HTTP status. Messages are always caller-safe (no provider internals).
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentFailureKind Kind { get; }

    /// <summary>Provider error name (best effort), e.g. "UNPROCESSABLE_ENTITY". May be null.</summary>
    public string? ProviderErrorName { get; }

    /// <summary>Provider field-level issue (best effort), e.g. an order-status violation string. May be null.</summary>
    public string? ProviderIssue { get; }

    /// <summary>
    /// Set when the provider order id is known despite the failure — lets the app persist
    /// a pending payment so a later call recovers the authorization instead of double-holding.
    /// </summary>
    public string? ProviderOrderId { get; set; }

    public PaymentGatewayException(PaymentFailureKind kind, string message, Exception? inner = null, string? providerErrorName = null, string? providerIssue = null)
        : base(message, inner)
    {
        Kind = kind;
        ProviderErrorName = providerErrorName;
        ProviderIssue = providerIssue;
    }
}
