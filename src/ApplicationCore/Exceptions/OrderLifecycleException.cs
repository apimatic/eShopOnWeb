using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an order's dispatch/cancel lifecycle is asked to make a transition it cannot make
/// (for example, dispatching an already-cancelled order).
/// </summary>
public class OrderLifecycleException : Exception
{
    public OrderLifecycleException(string message) : base(message)
    {
    }
}
