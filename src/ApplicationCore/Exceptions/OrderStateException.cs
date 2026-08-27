using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested order state transition is not valid for the order's current status.
/// </summary>
public class OrderStateException : Exception
{
    public OrderStateException(string message) : base(message)
    {
    }
}
