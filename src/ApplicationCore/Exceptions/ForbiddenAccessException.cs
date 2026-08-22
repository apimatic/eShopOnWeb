using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class ForbiddenAccessException : Exception
{
    public ForbiddenAccessException(string message) : base(message)
    {
    }
}
