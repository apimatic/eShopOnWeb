using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a requested resource does not exist, or exists but is not owned by
/// the calling shopper. The same exception is used for both cases deliberately, so
/// the API never reveals the existence of another shopper's resource.
/// </summary>
public class EntityNotFoundException : Exception
{
    public EntityNotFoundException(string message) : base(message)
    {
    }
}
