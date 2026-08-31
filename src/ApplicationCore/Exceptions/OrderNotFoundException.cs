using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an order cannot be found, or when the caller is not allowed to see it. The two cases are
/// deliberately indistinguishable so that one shopper cannot probe for another's orders.
/// </summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId)
        : base($"Order '{orderId}' was not found.")
    {
    }
}
