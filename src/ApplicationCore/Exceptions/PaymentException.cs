using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// An unexpected failure while talking to the payment processor (network error, malformed
/// response, or a PayPal error that is not an actionable business outcome). Business outcomes
/// that a caller can act on are returned as <c>Ardalis.Result</c> values, not thrown.
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(string message) : base(message)
    {
    }

    public PaymentException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
