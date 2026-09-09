using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when subscription billing operations fail.
/// </summary>
public class MaxioBillingException : Exception
{
    public MaxioBillingException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
