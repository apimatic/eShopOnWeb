using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The order request cannot be placed as given (no lines, bad quantity, unknown catalog item).
/// The message is safe to show to the caller.
/// </summary>
public class OrderValidationException : Exception
{
    public OrderValidationException(string message) : base(message)
    {
    }
}
