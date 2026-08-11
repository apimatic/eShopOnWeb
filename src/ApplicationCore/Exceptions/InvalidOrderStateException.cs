using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an operation is attempted against an order or payment that is not in a
/// valid state for that operation (e.g. fulfilling an order that was never authorized).
/// </summary>
public class InvalidOrderStateException : Exception
{
    public InvalidOrderStateException(string message) : base(message)
    {
    }
}
