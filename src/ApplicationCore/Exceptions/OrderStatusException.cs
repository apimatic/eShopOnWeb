using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an order lifecycle transition (dispatch / cancel) is not valid for the order's current state.
/// </summary>
public class OrderStatusException : Exception
{
    public OrderStatusException(string message) : base(message)
    {
    }
}
