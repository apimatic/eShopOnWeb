using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a requested resource does not exist or does not belong to the caller. Mapped to HTTP 404.
/// Using the same "not found" for "not yours" avoids leaking whether another shopper's resource exists.
/// </summary>
public class ResourceNotFoundException : Exception
{
    public ResourceNotFoundException(string message) : base(message)
    {
    }
}
