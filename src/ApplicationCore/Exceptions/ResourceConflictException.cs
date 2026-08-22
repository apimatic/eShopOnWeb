using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class ResourceConflictException : Exception
{
    public ResourceConflictException(string message) : base(message)
    {
    }
}
