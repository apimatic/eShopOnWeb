using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Raised when an order is placed with no line items.</summary>
public class EmptyOrderException : Exception
{
    public EmptyOrderException() : base("An order must contain at least one item.")
    {
    }
}
