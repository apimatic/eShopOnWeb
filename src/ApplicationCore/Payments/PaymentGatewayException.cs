using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// How a payment-gateway failure should be presented to the caller. Keeps a provider 4xx
/// (the caller sent something the provider rejected) distinct from a provider/transport 5xx.
/// </summary>
public enum PaymentFailureKind
{
    /// <summary>The provider rejected the request (e.g. declined card, invalid input). Surface as a client 4xx.</summary>
    Rejected = 0,

    /// <summary>A conflict / invalid state at the provider. Surface as 409.</summary>
    Conflict = 1,

    /// <summary>The provider is unreachable, errored, or returned an unreadable response. Surface as 5xx.</summary>
    Provider = 2
}

/// <summary>
/// Single failure type raised at the payment integration boundary. Carries only a caller-safe
/// message plus an optional operator-facing detail; never surfaces SDK/JSON exception text.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, PaymentFailureKind kind, int? providerStatusCode = null,
        string? operatorDetail = null, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
        ProviderStatusCode = providerStatusCode;
        OperatorDetail = operatorDetail;
    }

    public PaymentFailureKind Kind { get; }

    /// <summary>The HTTP status PayPal returned, when there was one.</summary>
    public int? ProviderStatusCode { get; }

    /// <summary>Extra detail an operator can act on (e.g. a PayPal issue code). Not shown to shoppers.</summary>
    public string? OperatorDetail { get; }
}

/// <summary>
/// A stale authorization that can no longer be renewed (reauthorized), so the order cannot be
/// fulfilled as-is. Reported in operator terms so an operator can act on it.
/// </summary>
public sealed class AuthorizationNotRenewableException : PaymentGatewayException
{
    public AuthorizationNotRenewableException(string message, string? operatorDetail = null, Exception? inner = null)
        : base(message, PaymentFailureKind.Conflict, operatorDetail: operatorDetail, inner: inner)
    {
    }
}
