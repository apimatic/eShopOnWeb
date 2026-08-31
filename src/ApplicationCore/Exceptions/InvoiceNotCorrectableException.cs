using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a caller tries to correct a bill that has already been put to the shopper or withdrawn.
/// The correction is refused rather than silently doing nothing.
/// </summary>
public class InvoiceNotCorrectableException : Exception
{
    public InvoiceNotCorrectableException(int invoiceId, InvoiceStatus status)
        : base($"Invoice {invoiceId} cannot be corrected because it is {status}. " +
               "Only a bill that has not yet been put to the shopper can be corrected.")
    {
    }

    public InvoiceNotCorrectableException(string message) : base(message)
    {
    }

    public InvoiceNotCorrectableException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
