using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Classifies a payment-gateway failure so the API boundary can map it to a coherent status.</summary>
public enum PaymentGatewayFailureKind
{
    /// <summary>The caller's request was rejected (validation/conflict/not-found) — they can act on it.</summary>
    CallerError = 0,
    /// <summary>Our credentials/quota or the provider itself — the caller cannot fix it.</summary>
    ProviderUnavailable = 1,
    /// <summary>A write whose outcome could not be determined.</summary>
    Unknown = 2
}

/// <summary>
/// A single, caller-safe failure type for every PayPal interaction. It carries the discriminators
/// the boundary keys on (HTTP status, provider error name, correlation/debug id) but never leaks
/// raw SDK/exception text or any card data.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(
        string message,
        PaymentGatewayFailureKind kind,
        int? statusCode = null,
        string? providerErrorName = null,
        string? debugId = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        StatusCode = statusCode;
        ProviderErrorName = providerErrorName;
        DebugId = debugId;
    }

    public PaymentGatewayFailureKind Kind { get; }
    public int? StatusCode { get; }
    public string? ProviderErrorName { get; }
    /// <summary>PayPal's <c>debug_id</c> for correlation.</summary>
    public string? DebugId { get; }
}

/// <summary>
/// Thrown when PayPal answers a card payment with a challenge that requires the shopper to approve
/// in a browser (e.g. 3DS / PAYER_ACTION_REQUIRED). Per the integration mandate we STOP and report
/// this rather than building an approval round-trip.
/// </summary>
public class PaymentChallengeRequiredException : PaymentGatewayException
{
    public PaymentChallengeRequiredException(string message, string? debugId = null)
        : base(message, PaymentGatewayFailureKind.CallerError, 422, "PAYER_ACTION_REQUIRED", debugId)
    {
    }
}
