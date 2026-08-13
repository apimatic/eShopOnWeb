using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a requested resource does not exist, or exists but is not visible to the caller.
/// Cross-owner access is deliberately reported as "not found" so one shopper cannot probe for
/// another shopper's resources. Surfaces to callers as HTTP 404 Not Found.
/// </summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message)
    {
    }
}
