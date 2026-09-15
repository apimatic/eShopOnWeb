using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class InvalidContactNumberException : Exception
{
    public InvalidContactNumberException(string message) : base(message)
    {
    }
}
