using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Wraps an error returned by the payment gateway (PayPal) so calling code and the API's exception
/// middleware can surface it without depending on gateway-specific wire types.
/// </summary>
public class PaymentGatewayException : Exception
{
    public string? ErrorName { get; }
    public string? DebugId { get; }
    public IReadOnlyList<string> Issues { get; }

    public PaymentGatewayException(string message, string? errorName = null, string? debugId = null, IReadOnlyList<string>? issues = null)
        : base(message)
    {
        ErrorName = errorName;
        DebugId = debugId;
        Issues = issues ?? Array.Empty<string>();
    }
}

/// <summary>
/// Thrown when the payment gateway requires the shopper to complete a browser approval step
/// (e.g. a 3-D Secure / SCA challenge) that this headless, server-side integration does not support.
/// </summary>
public class PaymentActionRequiredException : PaymentGatewayException
{
    public PaymentActionRequiredException(string message) : base(message)
    {
    }
}

/// <summary>
/// Thrown when a stale payment authorization can no longer be renewed (reauthorized) by PayPal,
/// and fulfilment cannot proceed without collecting a new payment from the shopper.
/// </summary>
public class PaymentAuthorizationNotRenewableException : PaymentGatewayException
{
    public PaymentAuthorizationNotRenewableException(string message) : base(message)
    {
    }
}
