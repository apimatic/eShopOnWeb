using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class EntityNotFoundException : Exception
{
    public EntityNotFoundException(string name)
        : base($"{name} was not found.")
    {
    }
}
