using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested entity does not exist — or does not belong to the caller, which is deliberately
/// reported the same way so one shopper cannot probe for another's orders or saved cards.
/// Surfaced to the API as 404 Not Found.
/// </summary>
public class EntityNotFoundException : Exception
{
    public EntityNotFoundException(string message) : base(message)
    {
    }
}
