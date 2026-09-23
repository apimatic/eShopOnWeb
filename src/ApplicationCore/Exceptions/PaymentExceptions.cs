using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Base of the payment-flow exceptions. Each carries a caller-safe message and maps to a distinct HTTP
/// status at the web boundary (see the API's exception middleware). No provider/SDK internals leak through
/// <see cref="Exception.Message"/>.
/// </summary>
public abstract class PaymentException : Exception
{
    protected PaymentException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>The caller sent something invalid (bad amount, missing card, refund exceeds captured). → 400/422.</summary>
public sealed class PaymentValidationException : PaymentException
{
    public PaymentValidationException(string message) : base(message) { }
}

/// <summary>The order/payment/saved-card does not exist or is not the caller's. → 404.</summary>
public sealed class PaymentNotFoundException : PaymentException
{
    public PaymentNotFoundException(string message) : base(message) { }
}

/// <summary>The action is not valid for the payment's current state (e.g. capture before authorize). → 409.</summary>
public sealed class PaymentConflictException : PaymentException
{
    public PaymentConflictException(string message) : base(message) { }
}

/// <summary>
/// PayPal answered the card with a challenge that needs shopper approval in a browser (3DS /
/// PAYER_ACTION_REQUIRED). Per the integration mandate we do not build an approval round-trip; the
/// operation stops and reports this. → 402.
/// </summary>
public sealed class PaymentChallengeRequiredException : PaymentException
{
    public PaymentChallengeRequiredException(string message) : base(message) { }
}

/// <summary>
/// A stale authorization could no longer be renewed before fulfilment, so the money cannot be taken.
/// Stated in terms an operator can act on. → 409.
/// </summary>
public sealed class AuthorizationExpiredException : PaymentException
{
    public AuthorizationExpiredException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// A PayPal call failed (provider error or unreachable). Carries an optional provider correlation id
/// (PayPal debug_id) for support, never the raw provider body. → 502/504.
/// </summary>
public sealed class PaymentGatewayException : PaymentException
{
    public string? DebugId { get; }

    public PaymentGatewayException(string message, string? debugId = null, Exception? inner = null)
        : base(message, inner) => DebugId = debugId;
}
