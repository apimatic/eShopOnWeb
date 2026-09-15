using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment operation could not proceed because of business state (e.g. paying an order in the wrong
/// state, refunding more than was captured). The message is safe to surface to the caller.
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(string message) : base(message) { }
}

/// <summary>
/// Fulfilment could not capture because the authorization had gone stale and could no longer be renewed.
/// The message is phrased so an operator knows what to do (re-collect payment from the shopper).
/// </summary>
public class AuthorizationExpiredException : Exception
{
    public AuthorizationExpiredException(string message) : base(message) { }
}

/// <summary>
/// PayPal answered a card payment with a challenge that requires the shopper to approve in a browser
/// (3-D Secure / <c>PAYER_ACTION_REQUIRED</c>). This integration deliberately does not build an
/// approval round-trip; it reports the challenge instead.
/// </summary>
public class PaymentChallengeRequiredException : Exception
{
    public PaymentChallengeRequiredException(string message) : base(message) { }
}
