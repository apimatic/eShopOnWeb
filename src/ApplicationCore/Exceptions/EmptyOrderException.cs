using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Thrown when an order is placed with no lines or a non-positive quantity.</summary>
public class EmptyOrderException : Exception
{
    public EmptyOrderException(string message) : base(message)
    {
    }
}
