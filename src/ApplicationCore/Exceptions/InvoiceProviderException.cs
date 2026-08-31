using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the invoicing provider could not be reached or returned an unexpected error
/// that is not a legitimate refusal of a state transition (transport failures, 5xx, etc.).
/// </summary>
public class InvoiceProviderException : Exception
{
    public InvoiceProviderException(string message) : base(message)
    {
    }

    public InvoiceProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
