using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a payment operation cannot be completed — either the payment processor
/// declined it or returned an error. Carries an optional debug id from the processor to aid
/// support without exposing sensitive data.
/// </summary>
public class PaymentProcessingException : Exception
{
    public PaymentProcessingException(string message, string? debugId = null)
        : base(message)
    {
        DebugId = debugId;
    }

    public PaymentProcessingException(string message, Exception innerException, string? debugId = null)
        : base(message, innerException)
    {
        DebugId = debugId;
    }

    /// <summary>PayPal debug id (from the error response), if available.</summary>
    public string? DebugId { get; }
}
