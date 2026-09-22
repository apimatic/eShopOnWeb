using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>How an application-level (non-PayPal) payment failure maps to an HTTP status.</summary>
public enum PaymentFlowError
{
    /// <summary>The order / payment / saved card does not exist.</summary>
    NotFound,

    /// <summary>The resource exists but belongs to another shopper.</summary>
    Forbidden,

    /// <summary>The operation is not valid for the current state (e.g. fulfilling an unpaid order).</summary>
    Conflict,

    /// <summary>The request itself is invalid (e.g. empty basket, refund over captured, no card supplied).</summary>
    Validation
}

/// <summary>An application-level payment error, mapped to an HTTP status by the endpoint layer.</summary>
public class PaymentFlowException : Exception
{
    public PaymentFlowException(PaymentFlowError error, string message) : base(message)
    {
        Error = error;
    }

    public PaymentFlowError Error { get; }
}
