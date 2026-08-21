using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Base type for every payment-flow failure the API surfaces to a caller. Each subtype maps to a
/// distinct HTTP status at the endpoint boundary so a client can tell "you asked for something
/// invalid" apart from "the provider is unavailable".
/// </summary>
public abstract class PaymentException : Exception
{
    protected PaymentException(string message) : base(message) { }
    protected PaymentException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// The requested payment operation is not valid for the order's current state (e.g. capturing an
/// order that was never authorized, or refunding more than was captured). Maps to 409 Conflict.
/// </summary>
public class InvalidPaymentOperationException : PaymentException
{
    public InvalidPaymentOperationException(string message) : base(message) { }
}

/// <summary>
/// PayPal rejected or could not process the request (a provider-side error). The message is a
/// caller-safe summary — never the raw provider exception text. Maps to 502 Bad Gateway.
/// </summary>
public class PaymentGatewayException : PaymentException
{
    public PaymentGatewayException(string message) : base(message) { }
    public PaymentGatewayException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// The card payment returned a challenge that requires the shopper to approve it in a browser
/// (e.g. 3-D Secure). This integration does not build an approval round-trip; it stops and reports.
/// Maps to 402 Payment Required.
/// </summary>
public class PaymentActionRequiredException : PaymentException
{
    public PaymentActionRequiredException(string message) : base(message) { }
}

/// <summary>
/// An authorization went stale before fulfilment and could no longer be renewed. Carries an
/// operator-actionable message describing why PayPal refused the re-authorization. Maps to 409.
/// </summary>
public class AuthorizationRenewalException : PaymentException
{
    public AuthorizationRenewalException(string message) : base(message) { }
    public AuthorizationRenewalException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// The order or payment the caller referenced does not exist, or does not belong to the caller.
/// Deliberately does not distinguish "not yours" from "not found" so existence is not leaked.
/// Maps to 404 Not Found.
/// </summary>
public class PaymentEntityNotFoundException : PaymentException
{
    public PaymentEntityNotFoundException(string message) : base(message) { }
}
