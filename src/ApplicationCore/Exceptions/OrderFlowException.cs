using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class OrderFlowException : Exception
{
    public OrderFlowException(string message) : base(message)
    {
    }
}
