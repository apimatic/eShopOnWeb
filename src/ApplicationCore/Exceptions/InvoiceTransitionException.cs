using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a bill's eShop lifecycle refuses a transition the caller asked for — for example issuing a
/// bill that has already been withdrawn. This is an outcome of the state the bill is in, not an integration fault.
/// </summary>
public class InvoiceTransitionException : Exception
{
    public InvoiceTransitionException(int invoiceId, InvoiceStatus status, string action)
        : base($"Invoice {invoiceId} cannot be {action}d because it is {status}.")
    {
    }

    public InvoiceTransitionException(string message) : base(message)
    {
    }

    public InvoiceTransitionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
