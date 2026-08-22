using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class OrderTransitionException : Exception
{
    public OrderTransitionException(string message) : base(message)
    {
    }
}
