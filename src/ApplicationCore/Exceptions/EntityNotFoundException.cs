using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A referenced entity does not exist, or does not belong to the caller (returned as "not found"
/// rather than "forbidden" so one shopper cannot even probe for another's data). Maps to HTTP 404.
/// </summary>
public class EntityNotFoundException : Exception
{
    public EntityNotFoundException(string message) : base(message) { }
}
