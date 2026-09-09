using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an order does not exist, or does not belong to the caller. Surfaced to the API as a
/// 404 Not Found — a shopper is never told whether another shopper's order exists.
/// </summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId)
        : base($"No order with id {orderId} was found.")
    {
    }
}
