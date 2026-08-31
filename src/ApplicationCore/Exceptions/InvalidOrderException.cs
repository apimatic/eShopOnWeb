using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an order placement request is invalid (e.g. unknown catalog items, bad quantities).
/// </summary>
public class InvalidOrderException : Exception
{
    public InvalidOrderException(string message) : base(message)
    {
    }
}
