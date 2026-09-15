using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment operation could not proceed for a reason the caller/operator can act on
/// (wrong state, nothing to capture, an authorization that can no longer be renewed, etc.).
/// Surfaces as a 4xx with an actionable message rather than a generic 500.
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(string message) : base(message) { }

    public PaymentException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Requested payment resource (order payment / saved card) does not exist for the caller.</summary>
public class PaymentNotFoundException : Exception
{
    public PaymentNotFoundException(string message) : base(message) { }
}

/// <summary>
/// PayPal answered a card payment with a challenge that requires the shopper to approve in a
/// browser. Per the task this integration stops rather than building an approval round-trip.
/// </summary>
public class PaymentChallengeRequiredException : PaymentException
{
    public PaymentChallengeRequiredException(string message) : base(message) { }
}
