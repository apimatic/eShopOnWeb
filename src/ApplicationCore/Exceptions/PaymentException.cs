using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment business-rule violation that should surface to the caller as a 422 (for example,
/// refunding more than was captured, or an authorization that can no longer be renewed). The
/// message is written to be actionable by an operator.
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
