using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a payment operation cannot be completed. The message is written
/// for an operator or shopper to act on; <see cref="PayPalIssue"/> carries the
/// machine-readable PayPal issue code when one is available.
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(string message, string? payPalIssue = null, Exception? innerException = null)
        : base(message, innerException)
    {
        PayPalIssue = payPalIssue;
    }

    public string? PayPalIssue { get; }
}
