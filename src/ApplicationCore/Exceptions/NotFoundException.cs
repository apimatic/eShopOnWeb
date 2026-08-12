using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an entity cannot be found, or when the caller is not entitled to it.
/// Ownership failures are surfaced as "not found" so the API never reveals the existence
/// of another shopper's data.
/// </summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message)
    {
    }
}
