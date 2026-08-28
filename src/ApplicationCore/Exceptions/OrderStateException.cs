using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// An operation was attempted against an order or payment whose current state does not allow it —
/// for example capturing an order that was already cancelled. Surfaces as HTTP 409.
/// </summary>
public class OrderStateException : Exception
{
    public OrderStateException(string message) : base(message)
    {
    }

    public OrderStateException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
