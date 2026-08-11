using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a payment operation cannot be completed. The message is written to be
/// actionable by an operator (e.g. an authorization that can no longer be renewed).
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(string message) : base(message)
    {
    }

    public PaymentException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// True when PayPal rejected the call because its idempotency key (PayPal-Request-Id) was
    /// already used — meaning the operation already went through and was not repeated.
    /// </summary>
    public virtual bool IsDuplicateRequest => false;
}
