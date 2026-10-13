using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Thrown when an order is placed with no order lines.</summary>
public class EmptyOrderException : Exception
{
    public EmptyOrderException()
        : base("An order must contain at least one item.")
    {
    }
}
