using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Raised when a PayPal operation fails in a way the caller/operator should be told about.</summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message) : base(message) { }
    public PaymentGatewayException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Raised at fulfilment when the authorization has gone stale and can no longer be renewed, so the
/// operator must act (e.g. ask the shopper to pay again). Carries a message an operator can act on.
/// </summary>
public class AuthorizationNotRenewableException : PaymentGatewayException
{
    public AuthorizationNotRenewableException(string message) : base(message) { }
    public AuthorizationNotRenewableException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Raised when PayPal answers a card payment with a challenge that requires a shopper to approve in
/// a browser (e.g. 3-D Secure). This integration is browser-less by design, so we surface it rather
/// than building an approval round-trip.
/// </summary>
public class PaymentChallengeRequiredException : PaymentGatewayException
{
    public PaymentChallengeRequiredException(string message) : base(message) { }
}

/// <summary>
/// Raised by the gateway when capturing fails because the authorization has expired/gone stale, so
/// the caller can renew (reauthorize) it and retry.
/// </summary>
public class AuthorizationExpiredException : PaymentGatewayException
{
    public AuthorizationExpiredException(string message) : base(message) { }
    public AuthorizationExpiredException(string message, Exception innerException) : base(message, innerException) { }
}
