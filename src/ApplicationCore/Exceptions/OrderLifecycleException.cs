using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Raised when an order lifecycle transition is not allowed (e.g. dispatching a cancelled order).</summary>
public class OrderLifecycleException : Exception
{
    public OrderLifecycleException(string message) : base(message)
    {
    }
}
