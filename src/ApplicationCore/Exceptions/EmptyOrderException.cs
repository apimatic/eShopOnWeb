using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Raised when an order is placed with no lines.</summary>
public class EmptyOrderException : Exception
{
    public EmptyOrderException()
        : base("An order must contain at least one item.")
    {
    }

    public EmptyOrderException(string message) : base(message)
    {
    }

    public EmptyOrderException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
