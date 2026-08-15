using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A requested resource does not exist, or does not belong to the caller. The same exception is
/// used for "not yours" so that one shopper cannot probe for another's orders or saved cards by
/// distinguishing not-found from forbidden.
/// </summary>
public class EntityNotFoundException : Exception
{
    public EntityNotFoundException(string message) : base(message) { }
}
