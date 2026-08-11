using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Base type for payment errors that map to a specific HTTP status for the caller.</summary>
public class PaymentException : Exception
{
    public PaymentException(string message) : base(message) { }
    public PaymentException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>The requested resource (order, payment, saved card) was not found for this caller.</summary>
public class PaymentNotFoundException : PaymentException
{
    public PaymentNotFoundException(string message) : base(message) { }
}

/// <summary>The payment is not in a state that allows the requested operation.</summary>
public class PaymentStateException : PaymentException
{
    public PaymentStateException(string message) : base(message) { }
}

/// <summary>A refund would exceed the captured amount, or is otherwise invalid.</summary>
public class RefundNotAllowedException : PaymentException
{
    public RefundNotAllowedException(string message) : base(message) { }
}

/// <summary>
/// The authorization has gone stale and can no longer be renewed, so fulfilment cannot take
/// the money. The message is phrased so an operator knows what to do next.
/// </summary>
public class AuthorizationExpiredException : PaymentException
{
    public AuthorizationExpiredException(string message) : base(message) { }
    public AuthorizationExpiredException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// PayPal answered a card payment with a challenge that requires the shopper to approve in a
/// browser. Per the integration's scope this is surfaced rather than handled with an approval
/// round-trip.
/// </summary>
public class PaymentChallengeRequiredException : PaymentException
{
    public PaymentChallengeRequiredException(string message) : base(message) { }
}

/// <summary>A call to PayPal failed. Carries PayPal's status code and debug id for diagnosis.</summary>
public class PayPalApiException : PaymentException
{
    public int StatusCode { get; }
    public string? DebugId { get; }
    public string? PayPalName { get; }

    public PayPalApiException(string message, int statusCode, string? debugId, string? payPalName)
        : base(message)
    {
        StatusCode = statusCode;
        DebugId = debugId;
        PayPalName = payPalName;
    }
}
