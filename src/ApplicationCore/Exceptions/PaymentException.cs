using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A business-level payment failure that should surface to the caller as a clear,
/// actionable message (mapped to HTTP 4xx) rather than an opaque server error.
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(string message) : base(message) { }
    public PaymentException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Raised when PayPal answers a card payment with a challenge that would require a
/// shopper to approve in a browser. This integration deliberately does not build an approval
/// round-trip; the condition is reported instead.</summary>
public class PaymentChallengeRequiredException : PaymentException
{
    public PaymentChallengeRequiredException(string message) : base(message) { }
}

/// <summary>Raised when a hold can no longer be captured or renewed and an operator needs to
/// act (for example, re-collect payment from the shopper).</summary>
public class AuthorizationUnrenewableException : PaymentException
{
    public AuthorizationUnrenewableException(string message) : base(message) { }
}
