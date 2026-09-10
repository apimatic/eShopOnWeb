using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A requested entity was not found — or is not visible to the caller. Shopper-scoped lookups
/// throw this for another shopper's data too, so existence is never leaked.
/// </summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}
