using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an order does not exist, or does not belong to the requesting shopper. The two
/// cases are deliberately indistinguishable so a shopper cannot probe for others' orders.
/// </summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId)
        : base($"No order with id {orderId} was found for the current user.")
    {
    }
}
