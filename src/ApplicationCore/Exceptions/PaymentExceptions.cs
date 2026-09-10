using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Base for payment errors the caller should see as a definite outcome, each carrying the HTTP status
/// an API boundary should return. Distinct from <see cref="PaymentGatewayException"/>, which is a
/// provider transport/rejection failure.
/// </summary>
public abstract class PaymentException : Exception
{
    protected PaymentException(string message, int statusCode) : base(message)
    {
        StatusCode = statusCode;
    }

    /// <summary>The HTTP status an API boundary should map this to.</summary>
    public int StatusCode { get; }
}

/// <summary>The order or payment the caller referred to does not exist (or is not theirs to see).</summary>
public sealed class PaymentResourceNotFoundException : PaymentException
{
    public PaymentResourceNotFoundException(string message) : base(message, 404) { }
}

/// <summary>The requested action is not valid for the payment's current state (e.g. fulfil before pay).</summary>
public sealed class PaymentStateException : PaymentException
{
    public PaymentStateException(string message) : base(message, 409) { }
}

/// <summary>The card payment was declined by PayPal.</summary>
public sealed class PaymentDeclinedException : PaymentException
{
    public PaymentDeclinedException(string message) : base(message, 402) { }
}

/// <summary>
/// PayPal returned a challenge that requires the shopper to approve in a browser (e.g. 3DS). This
/// integration intentionally does not build a browser approval round-trip and stops here.
/// </summary>
public sealed class PaymentChallengeRequiredException : PaymentException
{
    public PaymentChallengeRequiredException(string message) : base(message, 409) { }
}

/// <summary>
/// A stale authorization could not be renewed before fulfilment. The message is written for an operator
/// to act on (e.g. the shopper must place and pay for a new order).
/// </summary>
public sealed class ReauthorizationFailedException : PaymentException
{
    public ReauthorizationFailedException(string message) : base(message, 409) { }
}

/// <summary>A refund would take the order past what was captured.</summary>
public sealed class RefundExceedsCaptureException : PaymentException
{
    public RefundExceedsCaptureException(string message) : base(message, 409) { }
}

/// <summary>The request was malformed (e.g. neither card nor saved-card supplied, or a bad amount).</summary>
public sealed class PaymentValidationException : PaymentException
{
    public PaymentValidationException(string message) : base(message, 400) { }
}
