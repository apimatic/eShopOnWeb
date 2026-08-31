using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message)
    {
    }
}
