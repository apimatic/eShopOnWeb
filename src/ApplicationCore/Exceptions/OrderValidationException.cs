using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an order cannot be built from the request (no lines, non-positive quantity, or an unknown
/// catalog item). Surfaced to the caller as a 400.
/// </summary>
public class OrderValidationException : Exception
{
    public OrderValidationException(string message) : base(message)
    {
    }
}
