using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an order/payment request is malformed — an unknown catalog item, a non-positive
/// quantity, or a payment that names neither a card nor a saved card. Maps to HTTP 400 Bad Request.
/// </summary>
public class OrderRequestInvalidException : Exception
{
    public OrderRequestInvalidException(string message) : base(message)
    {
    }
}
