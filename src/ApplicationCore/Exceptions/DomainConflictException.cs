using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class DomainConflictException : Exception
{
    public DomainConflictException(string message) : base(message)
    {
    }
}
