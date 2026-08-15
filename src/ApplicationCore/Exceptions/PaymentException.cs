using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment operation failed for a reason the caller/operator can act on (bad state, declined
/// card, an authorization that can no longer be renewed, a refund that would exceed the capture).
/// Surfaced to the API as a 4xx with the message.
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(string message) : base(message) { }

    public PaymentException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Raised when the processor answers a card payment with a challenge that would require the
/// shopper to approve in a browser (3DS / payer-action-required). Per the integration mandate we
/// STOP rather than build an approval round-trip; the API surfaces this so the caller sees it.
/// </summary>
public class PaymentChallengeRequiredException : PaymentException
{
    public PaymentChallengeRequiredException(string message) : base(message) { }
}

/// <summary>Raised when a shopper acts on an order/payment/saved-card that is not theirs, or that does not exist.</summary>
public class PaymentNotFoundException : Exception
{
    public PaymentNotFoundException(string message) : base(message) { }
}

/// <summary>
/// Raised when the payment provider is unreachable, times out, or returns something we cannot
/// process (transport failure, 5xx, or an unparseable body). Distinct from <see cref="PaymentException"/>
/// (a caller-actionable rejection); surfaced to the API as a 502 so a retry is meaningful.
/// </summary>
public class PaymentProviderUnavailableException : Exception
{
    public PaymentProviderUnavailableException(string message) : base(message) { }

    public PaymentProviderUnavailableException(string message, Exception inner) : base(message, inner) { }
}
