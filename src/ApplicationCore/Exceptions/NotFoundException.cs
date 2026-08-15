using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a requested resource does not exist, or exists but does not belong to the caller.
/// Deliberately does not distinguish the two so one shopper cannot probe another's data. Maps to
/// HTTP 404 Not Found at the API boundary.
/// </summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message)
    {
    }
}
