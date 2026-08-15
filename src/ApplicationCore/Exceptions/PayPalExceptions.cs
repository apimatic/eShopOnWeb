using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A PayPal interaction failed. Carries a caller-safe message (and, where available, PayPal's
/// debug id) but never any card data.
/// </summary>
public class PayPalGatewayException : Exception
{
    public PayPalGatewayException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

/// <summary>
/// PayPal answered a card payment with a challenge that requires the shopper to approve in a
/// browser (e.g. 3-D Secure / PAYER_ACTION_REQUIRED). Per the integration mandate we stop rather
/// than build an approval round-trip.
/// </summary>
public class PayPalChallengeRequiredException : PayPalGatewayException
{
    public PayPalChallengeRequiredException(string message) : base(message)
    {
    }
}

/// <summary>
/// A stale authorization can no longer be renewed (reauthorized), so fulfilment cannot proceed.
/// The message is phrased for an operator to act on.
/// </summary>
public class AuthorizationNotRenewableException : PayPalGatewayException
{
    public AuthorizationNotRenewableException(string message) : base(message)
    {
    }
}
