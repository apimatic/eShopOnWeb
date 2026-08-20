using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class OrderNotFoundException : Exception
{
    public OrderNotFoundException() : base("The order was not found.")
    {
    }
}
