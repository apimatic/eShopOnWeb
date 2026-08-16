using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a shopper-scoped resource (an order, a saved card) does not exist for the caller.
/// Not-owned resources are reported as not-found so their existence is never leaked to other shoppers.
/// </summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message)
    {
    }
}
