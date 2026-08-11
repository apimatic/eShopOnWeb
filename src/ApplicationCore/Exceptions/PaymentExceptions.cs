using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment/fulfilment operation was requested in a state the order's domain rules forbid
/// (e.g. cancelling an already-captured order, or refunding more than was captured).
/// Maps to an HTTP 409 Conflict.
/// </summary>
public class PaymentOperationException : Exception
{
    public PaymentOperationException(string message) : base(message) { }
}

/// <summary>
/// A resource the caller referenced (order or saved card) could not be found for that caller.
/// Kept deliberately owner-scoped so one shopper cannot probe another's data. Maps to HTTP 404.
/// </summary>
public class PaymentResourceNotFoundException : Exception
{
    public PaymentResourceNotFoundException(string message) : base(message) { }
}

/// <summary>
/// PayPal rejected a call. Carries the parsed PayPal error (name / debug id / issues) so an operator
/// can act on it. Maps to HTTP 502 Bad Gateway.
/// </summary>
public class PayPalApiException : Exception
{
    public int StatusCode { get; }
    public string? PayPalName { get; }
    public string? DebugId { get; }

    public PayPalApiException(string message, int statusCode, string? payPalName = null, string? debugId = null)
        : base(message)
    {
        StatusCode = statusCode;
        PayPalName = payPalName;
        DebugId = debugId;
    }
}

/// <summary>
/// The authorization holding the funds has expired and can no longer be renewed (reauthorized).
/// Fulfilment cannot proceed; the operator must obtain a fresh payment from the shopper.
/// Maps to HTTP 409 Conflict with an operator-actionable message.
/// </summary>
public class AuthorizationNotRenewableException : Exception
{
    public AuthorizationNotRenewableException(string message) : base(message) { }
}

/// <summary>
/// PayPal answered a card payment with a challenge that requires the shopper to approve in a browser
/// (e.g. a 3-D Secure step-up). Per the integration contract we STOP rather than build an approval
/// round-trip. Maps to HTTP 409 Conflict.
/// </summary>
public class PaymentChallengeRequiredException : Exception
{
    public PaymentChallengeRequiredException(string message) : base(message) { }
}
