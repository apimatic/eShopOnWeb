using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a requested resource does not exist, or exists but does not belong to the caller.
/// Ownership failures are surfaced as "not found" so one shopper cannot probe for the existence of
/// another shopper's orders or saved cards.
/// </summary>
public class EntityNotFoundException : Exception
{
    public EntityNotFoundException(string message) : base(message)
    {
    }
}
