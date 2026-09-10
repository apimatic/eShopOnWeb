using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>How a payment-provider failure should be understood by the caller.</summary>
public enum PaymentGatewayFailureKind
{
    /// <summary>Our credentials, quota, or the provider itself are at fault — the caller cannot fix it.</summary>
    ProviderUnavailable,
    /// <summary>The provider rejected the request the caller made — the caller can act on it.</summary>
    RequestRejected,
    /// <summary>The provider answered in a shape we could not read — outcome may be unknown.</summary>
    Unreadable
}

/// <summary>
/// A failure returned by (or reaching) the payment provider, translated at the Infrastructure boundary so
/// the rest of the application has a single failure type to handle. Carries only a caller-safe message;
/// the provider's correlation id (PayPal <c>debug_id</c>) is captured for logs, never surfaced to callers.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, PaymentGatewayFailureKind kind,
        HttpStatusCode? providerStatus = null, string? debugId = null, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
        ProviderStatus = providerStatus;
        DebugId = debugId;
    }

    public PaymentGatewayFailureKind Kind { get; }

    /// <summary>The HTTP status the provider returned, when one was available.</summary>
    public HttpStatusCode? ProviderStatus { get; }

    /// <summary>PayPal's correlation id for the failed call, for diagnostics.</summary>
    public string? DebugId { get; }
}
