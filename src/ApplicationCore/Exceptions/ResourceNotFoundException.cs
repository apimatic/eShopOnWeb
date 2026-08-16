using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested resource does not exist, or does not belong to the caller. We deliberately do not
/// distinguish "not found" from "belongs to someone else" so one shopper cannot probe for another's
/// orders or saved cards.
/// </summary>
public class ResourceNotFoundException : Exception
{
    public ResourceNotFoundException(string message) : base(message) { }
}
