using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A requested entity does not exist, or does not belong to the caller. Cross-owner access is
/// reported as "not found" so one shopper cannot probe for another's orders or saved cards.
/// Maps to HTTP 404 Not Found.
/// </summary>
public class EntityNotFoundException : Exception
{
    public EntityNotFoundException(string message) : base(message) { }
}
