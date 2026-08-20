using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message)
    {
    }
}
