using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>A requested payment/order resource does not exist (maps to HTTP 404).</summary>
public class PaymentNotFoundException : Exception
{
    public PaymentNotFoundException(string message) : base(message) { }
}

/// <summary>The caller is not allowed to see or act on this resource (maps to HTTP 403).</summary>
public class PaymentAccessDeniedException : Exception
{
    public PaymentAccessDeniedException(string message) : base(message) { }
}

/// <summary>
/// The operation is not valid for the resource's current state — e.g. fulfilling an unpaid order,
/// cancelling after fulfilment, or refunding beyond what was captured (maps to HTTP 409).
/// </summary>
public class PaymentConflictException : Exception
{
    public PaymentConflictException(string message) : base(message) { }
}

/// <summary>
/// The request was well-formed but cannot be satisfied — e.g. a stale authorization that can no
/// longer be renewed, described in terms an operator can act on (maps to HTTP 422).
/// </summary>
public class PaymentUnprocessableException : Exception
{
    public PaymentUnprocessableException(string message) : base(message) { }
}

/// <summary>
/// PayPal answered the card payment with a challenge that needs a shopper to approve in a browser.
/// Per the integration contract we stop rather than build an approval round-trip (maps to HTTP 422).
/// </summary>
public class PayPalChallengeRequiredException : Exception
{
    public PayPalChallengeRequiredException(string message) : base(message) { }
}

/// <summary>A PayPal API call failed. Carries PayPal's debug id and status for diagnosis (maps to HTTP 502).</summary>
public class PayPalApiException : Exception
{
    public int? PayPalStatusCode { get; }
    public string? DebugId { get; }
    public string? PayPalErrorName { get; }

    public PayPalApiException(string message, int? payPalStatusCode = null, string? debugId = null,
        string? payPalErrorName = null, Exception? inner = null)
        : base(message, inner)
    {
        PayPalStatusCode = payPalStatusCode;
        DebugId = debugId;
        PayPalErrorName = payPalErrorName;
    }
}
