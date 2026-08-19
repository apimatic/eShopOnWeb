using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Thrown when an entity an operation targets does not exist (or is not visible to the caller).</summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}
