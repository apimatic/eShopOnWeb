using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public enum PaymentErrorReason
{
    /// <summary>Bad/invalid request from the caller. → 400</summary>
    Validation,

    /// <summary>Resource does not exist, or the caller may not see it. → 404</summary>
    NotFound,

    /// <summary>Operation not allowed in the current state. → 409</summary>
    Conflict,

    /// <summary>PayPal answered with a challenge that needs a shopper to approve in a browser. → 422</summary>
    RequiresBuyerAction,

    /// <summary>Something failed downstream at PayPal. → 502</summary>
    ProviderError
}

/// <summary>
/// Raised for expected, actionable failures in the payment flows. Carries a reason that the API
/// layer maps to an HTTP status. The message is written to be actionable (e.g. for an operator).
/// </summary>
public class PaymentException : Exception
{
    public PaymentErrorReason Reason { get; }

    public PaymentException(string message, PaymentErrorReason reason, Exception? innerException = null)
        : base(message, innerException)
    {
        Reason = reason;
    }
}
