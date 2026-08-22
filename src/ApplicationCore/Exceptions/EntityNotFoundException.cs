using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class EntityNotFoundException : Exception
{
    public EntityNotFoundException(string message) : base(message)
    {
    }
}
