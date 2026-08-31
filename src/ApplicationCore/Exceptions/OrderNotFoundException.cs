using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an order cannot be found — or when it belongs to a different shopper. As with invoices,
/// the two cases are indistinguishable so a shopper cannot see or bill another's orders.
/// </summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId)
        : base($"Order '{orderId}' was not found.") { }
}
