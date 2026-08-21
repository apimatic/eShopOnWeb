using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class EntityNotFoundException : Exception
{
    public EntityNotFoundException(string name, object key)
        : base($"{name} '{key}' was not found.")
    {
    }
}
