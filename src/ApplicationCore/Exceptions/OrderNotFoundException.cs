using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested order does not exist, or does not belong to the caller. The two are
/// deliberately indistinguishable so a shopper can never probe for another shopper's orders.
/// Maps to 404 at the API boundary.
/// </summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId)
        : base($"Order {orderId} was not found.")
    {
    }
}
