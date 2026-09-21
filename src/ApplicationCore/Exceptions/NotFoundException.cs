using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A requested resource does not exist, or does not belong to the caller. Ownership failures are
/// reported as "not found" so one shopper cannot probe for another's orders or saved cards.
/// </summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message)
    {
    }
}
