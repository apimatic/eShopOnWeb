using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class BadRequestException : Exception
{
    public BadRequestException(string message) : base(message)
    {
    }
}
