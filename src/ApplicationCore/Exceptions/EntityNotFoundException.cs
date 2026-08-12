using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Raised when a referenced entity (order, notification) does not exist. Maps to HTTP 404.</summary>
public class EntityNotFoundException : Exception
{
    public EntityNotFoundException(string entity, int id)
        : base($"No {entity} found with id {id}.")
    {
    }
}
