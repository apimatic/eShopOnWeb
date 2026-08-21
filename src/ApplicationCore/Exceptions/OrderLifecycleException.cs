using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class OrderLifecycleException : Exception
{
    public OrderLifecycleException(string message) : base(message)
    {
    }
}
