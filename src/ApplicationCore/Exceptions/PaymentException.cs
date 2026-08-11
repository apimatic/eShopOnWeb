using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A business-rule failure in the payment flow that the caller (shopper or operator) can act on — for
/// example refunding more than was captured, or an authorization that can no longer be renewed. Maps to a
/// 4xx response, distinct from an unexpected server error.
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(string message) : base(message) { }
}

/// <summary>
/// Raised when PayPal answers a card payment with a challenge that requires the shopper to approve in a
/// browser. This integration deliberately does not build a browser approval round-trip, so the flow stops
/// and surfaces the condition instead.
/// </summary>
public class PaymentChallengeRequiredException : PaymentException
{
    public PaymentChallengeRequiredException()
        : base("PayPal requires the shopper to approve this card payment in a browser (a challenge/3-D Secure step). " +
               "This API does not support a browser approval round-trip; use a card that does not trigger a challenge.")
    { }
}

/// <summary>Raised when a shopper or operator refers to an order or saved card that is not theirs / not found.</summary>
public class PaymentResourceNotFoundException : PaymentException
{
    public PaymentResourceNotFoundException(string message) : base(message) { }
}
