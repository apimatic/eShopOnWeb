using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested payment operation conflicts with the current state of the
/// order/payment (e.g. paying an already-paid order, over-refunding a capture).
/// </summary>
public class PaymentStateException : Exception
{
    public PaymentStateException(string message) : base(message) { }
}

/// <summary>
/// PayPal declined or refused the payment operation.
/// </summary>
public class PaymentDeclinedException : Exception
{
    public PaymentDeclinedException(string message) : base(message) { }
}

/// <summary>
/// The authorization can no longer be renewed (e.g. it is older than the
/// 29-day reauthorization window). An operator must decide how to proceed
/// (e.g. cancel the order and ask the shopper to place it again).
/// </summary>
public class AuthorizationNotRenewableException : Exception
{
    public AuthorizationNotRenewableException(string message) : base(message) { }
}

/// <summary>
/// PayPal answered the payment with a challenge that requires the shopper to
/// approve in a browser (e.g. 3-D Secure). This integration does not support
/// approval round-trips.
/// </summary>
public class PaymentRequiresBuyerActionException : Exception
{
    public PaymentRequiresBuyerActionException(string message) : base(message) { }
}
