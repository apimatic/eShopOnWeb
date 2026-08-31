using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a caller attempts a transition a bill cannot accept in its current state — for example
/// correcting a bill that has already been put to the shopper or withdrawn. The caller is told so
/// explicitly rather than the change silently doing nothing.
/// </summary>
public class InvoiceNotModifiableException : Exception
{
    public InvoiceNotModifiableException(string providerInvoiceId, InvoiceState state, string action)
        : base($"Invoice '{providerInvoiceId}' is {state} and cannot be {action}. " +
               "This action is only possible while the bill has not yet been put to the shopper.")
    {
        ProviderInvoiceId = providerInvoiceId;
        State = state;
        Action = action;
    }

    public string ProviderInvoiceId { get; }
    public InvoiceState State { get; }
    public string Action { get; }
}
