using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the payment provider could not be reached or returned an unexpected error that is
/// not a legitimate refusal of the requested state transition.
/// </summary>
public class InvoicingProviderException : Exception
{
    public InvoicingProviderException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
