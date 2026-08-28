using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested entity does not exist, or does not belong to the caller. Surfaces as HTTP 404 —
/// deliberately indistinguishable from "not yours", so one shopper cannot probe another's data.
/// </summary>
public class EntityNotFoundException : Exception
{
    public EntityNotFoundException(string message) : base(message)
    {
    }
}
