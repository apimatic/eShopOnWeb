using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an order does not exist, or exists but is not owned by the requesting buyer. The same
/// exception is used for both cases so a shopper cannot distinguish "not yours" from "does not exist".
/// </summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId)
        : base($"No order found with id {orderId}.")
    {
    }
}
