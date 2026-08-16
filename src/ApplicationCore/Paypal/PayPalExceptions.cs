using System;

namespace Microsoft.eShopWeb.ApplicationCore.Paypal;

/// <summary>A PayPal API call failed. Carries PayPal's debug_id, which is required for support.</summary>
public class PayPalApiException : Exception
{
    public PayPalApiException(string message, int statusCode, string? debugId = null,
        string? name = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        DebugId = debugId;
        Name = name;
    }

    public int StatusCode { get; }
    public string? DebugId { get; }
    /// <summary>PayPal error name, e.g. INSTRUMENT_DECLINED, UNPROCESSABLE_ENTITY.</summary>
    public string? Name { get; }
}

/// <summary>
/// PayPal answered a card payment with a challenge that needs a shopper to approve in a browser
/// (e.g. a 3-D Secure challenge). Per the task this is a hard stop, not something to build an
/// approval round-trip for.
/// </summary>
public class PaymentApprovalRequiredException : Exception
{
    public PaymentApprovalRequiredException(string message) : base(message)
    {
    }
}

/// <summary>
/// An authorization that has gone stale can no longer be renewed (re-authorized). Carries a
/// message an operator can act on.
/// </summary>
public class AuthorizationNotRenewableException : Exception
{
    public AuthorizationNotRenewableException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}
