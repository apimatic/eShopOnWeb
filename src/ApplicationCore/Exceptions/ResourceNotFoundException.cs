using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class ResourceNotFoundException : Exception
{
    public ResourceNotFoundException(string message) : base(message)
    {
    }
}
