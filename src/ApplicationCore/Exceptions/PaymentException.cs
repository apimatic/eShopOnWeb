using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Domain/payment failure with an HTTP status an API caller can act on.
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(string message, int statusCode = 400, string? paypalDebugId = null, string? paypalIssue = null)
        : base(message)
    {
        StatusCode = statusCode;
        PayPalDebugId = paypalDebugId;
        PayPalIssue = paypalIssue;
    }

    public int StatusCode { get; }
    public string? PayPalDebugId { get; }
    public string? PayPalIssue { get; }
}

/// <summary>
/// PayPal required a shopper to complete a browser challenge (3-D Secure / payer-action).
/// This integration does not implement that round-trip.
/// </summary>
public class PayerActionRequiredException : PaymentException
{
    public PayerActionRequiredException(string message, string? paypalDebugId = null)
        : base(message, 409, paypalDebugId, "PAYER_ACTION_REQUIRED")
    {
    }
}
