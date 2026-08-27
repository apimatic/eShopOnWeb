using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested operation conflicts with the order's or payment's current state.
/// Maps to HTTP 409 at the API boundary.
/// </summary>
public class OrderStateException : Exception
{
    public OrderStateException(string message) : base(message)
    {
    }
}
