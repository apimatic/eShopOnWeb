using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an order cannot be found for the caller. Also used when an order exists but belongs to
/// another shopper, so that one shopper cannot raise a bill against another's order. Surfaces as HTTP
/// 404 Not Found.
/// </summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(string message) : base(message)
    {
    }
}
