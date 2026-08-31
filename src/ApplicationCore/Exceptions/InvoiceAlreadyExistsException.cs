using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a bill is raised for an order that already has a live (non-withdrawn) bill, to avoid
/// double-billing the same order.
/// </summary>
public class InvoiceAlreadyExistsException : Exception
{
    public InvoiceAlreadyExistsException(int orderId)
        : base($"Order {orderId} already has a live invoice. Withdraw it before raising another.")
    {
    }

    public InvoiceAlreadyExistsException(string message) : base(message)
    {
    }

    public InvoiceAlreadyExistsException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
