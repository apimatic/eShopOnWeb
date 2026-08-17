using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>A requested entity does not exist, or is not visible to the caller (scoped away).</summary>
public class EntityNotFoundException : Exception
{
    public EntityNotFoundException(string message) : base(message) { }
}
