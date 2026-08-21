using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a shopper-scoped resource (order or saved card) does not exist for the caller.
/// Deliberately does not distinguish "not found" from "not yours" so one shopper cannot probe
/// for another's data.
/// </summary>
public class EntityNotFoundException : Exception
{
    public EntityNotFoundException(string message) : base(message) { }
}
