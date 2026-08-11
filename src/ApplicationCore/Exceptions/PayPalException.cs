using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A PayPal API call failed. Carries what PayPal reported so the message can be surfaced to an operator.
/// </summary>
public class PayPalException : Exception
{
    public int? HttpStatusCode { get; }
    public string? IssueName { get; }
    public string? DebugId { get; }

    public PayPalException(string message, int? httpStatusCode = null, string? issueName = null,
        string? debugId = null, Exception? inner = null)
        : base(message, inner)
    {
        HttpStatusCode = httpStatusCode;
        IssueName = issueName;
        DebugId = debugId;
    }
}

/// <summary>
/// PayPal answered a card payment with a challenge that requires the shopper to approve in a browser
/// (e.g. 3-D Secure). This app deliberately does not build an approval round-trip — the flow stops here.
/// </summary>
public class PayPalChallengeException : PayPalException
{
    public PayPalChallengeException(string message, string? debugId = null)
        : base(message, httpStatusCode: 402, issueName: "PAYER_ACTION_REQUIRED", debugId: debugId)
    {
    }
}

/// <summary>
/// An authorization can no longer be used (it has expired or been voided and cannot be renewed),
/// so fulfilment cannot proceed. The message is phrased so an operator knows what to do next.
/// </summary>
public class AuthorizationUnusableException : PayPalException
{
    public AuthorizationUnusableException(string message)
        : base(message, httpStatusCode: 409)
    {
    }
}
