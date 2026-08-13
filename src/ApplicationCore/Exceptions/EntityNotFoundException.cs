using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a requested aggregate does not exist (maps to HTTP 404).
/// Deliberately does not distinguish "missing" from "not yours" so that a caller
/// cannot probe for the existence of another shopper's data.
/// </summary>
public class EntityNotFoundException : Exception
{
    public EntityNotFoundException(string message) : base(message)
    {
    }
}
