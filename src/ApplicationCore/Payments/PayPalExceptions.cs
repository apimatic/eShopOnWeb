using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Base for any error surfaced by the PayPal integration.</summary>
public class PayPalGatewayException : Exception
{
    public int? StatusCode { get; }
    public string? PayPalName { get; }
    public string? DebugId { get; }

    public PayPalGatewayException(string message, int? statusCode = null, string? payPalName = null, string? debugId = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        PayPalName = payPalName;
        DebugId = debugId;
    }
}

/// <summary>
/// PayPal answered a card payment with a challenge that requires a shopper to approve in a browser
/// (e.g. 3-D Secure). Per the task this is reported, not worked around with an approval round-trip.
/// </summary>
public class PayPalCardChallengeRequiredException : PayPalGatewayException
{
    public PayPalCardChallengeRequiredException(string message, string? debugId = null)
        : base(message, statusCode: null, payPalName: "PAYER_ACTION_REQUIRED", debugId: debugId)
    {
    }
}

/// <summary>
/// An authorization has gone stale and PayPal will no longer renew it, so the order cannot be
/// fulfilled against it. The message is phrased for an operator to act on.
/// </summary>
public class PayPalAuthorizationUnrenewableException : PayPalGatewayException
{
    public PayPalAuthorizationUnrenewableException(string message, string? payPalName = null, string? debugId = null)
        : base(message, statusCode: null, payPalName: payPalName, debugId: debugId)
    {
    }
}
