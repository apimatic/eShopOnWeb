using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The order (or its payment) is in a state that does not allow the requested operation.
/// Maps to HTTP 409 at the API boundary.
/// </summary>
public class OrderStateException : Exception
{
    public OrderStateException(string message) : base(message)
    {
    }
}
