using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Raised when the payment processor rejects an operation. <see cref="IsOperatorActionable"/>
/// marks failures an operator can act on (e.g. an authorization that can no longer be renewed),
/// so the API can surface the message rather than a generic 500.
/// </summary>
public class PaymentGatewayException : Exception
{
    public bool IsOperatorActionable { get; }
    public string? DebugId { get; }

    public PaymentGatewayException(string message, bool isOperatorActionable = false, string? debugId = null, Exception? inner = null)
        : base(message, inner)
    {
        IsOperatorActionable = isOperatorActionable;
        DebugId = debugId;
    }
}

/// <summary>
/// Raised by a capture attempt when the underlying authorization has gone stale/expired and must be
/// renewed before the money can be taken. The caller reauthorizes and retries the capture.
/// </summary>
public class AuthorizationExpiredException : PaymentGatewayException
{
    public AuthorizationExpiredException(string message, string? debugId = null, Exception? inner = null)
        : base(message, isOperatorActionable: false, debugId: debugId, inner: inner)
    {
    }
}

/// <summary>
/// Raised when a card payment needs a shopper to approve it in a browser (a 3-D Secure style
/// challenge). Per the integration's contract this is surfaced, not worked around with an
/// approval round-trip.
/// </summary>
public class PaymentRequiresCustomerActionException : PaymentGatewayException
{
    public PaymentRequiresCustomerActionException(string message)
        : base(message, isOperatorActionable: false)
    {
    }
}
