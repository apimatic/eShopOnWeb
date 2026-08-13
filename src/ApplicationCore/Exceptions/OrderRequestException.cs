using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an order request is malformed — empty, bad quantities, or unknown catalog items
/// (maps to HTTP 400).
/// </summary>
public class OrderRequestException : Exception
{
    public OrderRequestException(string message) : base(message)
    {
    }
}
